using Godot;
using Nerdbank.MessagePack;
using PolyType;
using PolyType.ReflectionProvider;
using System;
using System.Collections.Generic;
using System.Reflection;

[GenerateShape]
public partial record RPC
{

    public string MethodName;

    public string NodePath;

    public ulong ObjectID;

    // Each argument is serialized independently with its own concrete type so the
    // type identity survives the wire. A null element means the argument was null.
    public byte[][] args;
}

/// <summary>
/// Marks a method as callable through <see cref="RPCManager"/>. Methods without it are never invoked
/// remotely, even if a peer names them.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RPCAttribute : Attribute
{
    /// <summary>
    /// Drop the call unless it was sent by the target's authority: the target GMPObject's
    /// <c>authority</c>, or the lobby host for non-GMPObject targets (e.g. level props).
    /// Use this on the "apply the outcome" half of an authority-arbitrated action.
    /// </summary>
    public bool requireAuthority { get; set; }
}

/// <summary>
/// Reflection-based RPCs over <see cref="Channel.RPC_Main"/> (always reliable). A target is addressed by
/// node path or by GMPObject id; the method is looked up by name and must carry <see cref="RPCAttribute"/>.
/// <para>
/// <see cref="RPC(Node, string, object[])"/> sends to every peer <b>including this one</b>, and the local copy
/// is delivered synchronously before the call returns. <see cref="RPCTo(ulong, Node, string, object[])"/>
/// sends to one peer. For actions where only one of several simultaneous attempts may win, use
/// <see cref="RequestFromAuthority"/>: request → the authority validates (first wins) → the authority
/// broadcasts the outcome to a <c>[RPC(requireAuthority = true)]</c> method.
/// </para>
/// </summary>
public partial class RPCManager : Node
{
    public static MessagePackSerializer s;

    /// <summary>
    /// The peer that sent the RPC currently being handled (0 outside a handler). Comes from the
    /// transport, so unlike a peer id passed as an argument it cannot be spoofed.
    /// </summary>
    public static ulong sender { get; private set; }

    private static readonly Dictionary<(Type, string), MethodInfo> methodCache = new();

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        ObjectConverterOptions opt = new ObjectConverterOptions();
        opt.PreserveIntegerTypes = true;
        s = new MessagePackSerializer().WithObjectConverter(opt);
        Lobby.ConnectedToHostEvent += Instance_ConnectedToHostEvent;
    }

    private void Instance_ConnectedToHostEvent(ulong HostID)
    {
        Lobby.network.MessageReceivedEvent += OnMessageReceived;
    }

    // ---- sending ----------------------------------------------------------

    /// <summary>Calls <paramref name="methodName"/> on <paramref name="targetNode"/> on every peer, including this one.</summary>
    public static Error RPC(Node targetNode, string methodName, object[] args)
    {
        return Lobby.SendToAllAndSelf(Channel.RPC_Main, Pack(methodName, targetNode.GetPath(), 0, args));
    }

    /// <summary>Calls <paramref name="methodName"/> on the GMPObject <paramref name="objectID"/> on every peer, including this one.</summary>
    public static Error RPC(ulong objectID, string methodName, object[] args)
    {
        return Lobby.SendToAllAndSelf(Channel.RPC_Main, Pack(methodName, null, objectID, args));
    }

    /// <summary>Calls <paramref name="methodName"/> on the node at <paramref name="nodePath"/> on every peer, including this one.</summary>
    public static Error RPC(string nodePath, string methodName, object[] args)
    {
        return Lobby.SendToAllAndSelf(Channel.RPC_Main, Pack(methodName, nodePath, 0, args));
    }

    /// <summary>Calls <paramref name="methodName"/> on <paramref name="targetNode"/> on one peer only (may be this peer).</summary>
    public static Error RPCTo(ulong peer, Node targetNode, string methodName, object[] args)
    {
        return Lobby.Send(peer, Channel.RPC_Main, Pack(methodName, targetNode.GetPath(), 0, args));
    }

    /// <summary>Calls <paramref name="methodName"/> on the GMPObject <paramref name="objectID"/> on one peer only (may be this peer).</summary>
    public static Error RPCTo(ulong peer, ulong objectID, string methodName, object[] args)
    {
        return Lobby.Send(peer, Channel.RPC_Main, Pack(methodName, null, objectID, args));
    }

    /// <summary>
    /// Sends a request to <paramref name="target"/>'s authority, which decides the outcome for everyone.
    /// The request handler should validate against the authority's own state (reading <see cref="sender"/>
    /// for who asked) and, if accepted, broadcast the outcome with <see cref="RPC(ulong, string, object[])"/>
    /// to a <c>[RPC(requireAuthority = true)]</c> method.
    /// </summary>
    public static Error RequestFromAuthority(GMPObject target, string methodName, object[] args)
    {
        return RPCTo(target.authority, target.id, methodName, args);
    }

    private static byte[] Pack(string methodName, string nodePath, ulong objectID, object[] args)
    {
        RPC rpc = new();
        rpc.MethodName = methodName;
        rpc.NodePath = nodePath;
        rpc.ObjectID = objectID;
        rpc.args = SerializeArgs(args);
        return s.Serialize(rpc);
    }

    // Serialize each argument by its concrete runtime type so type identity is
    // preserved on the wire (a null argument becomes a null element).
    private static byte[][] SerializeArgs(object[] args)
    {
        var result = new byte[args.Length][];
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] is null)
            {
                result[i] = null;
                continue;
            }
            ITypeShape shape = ReflectionTypeShapeProvider.Default.GetTypeShape(args[i].GetType());
            result[i] = s.SerializeObject(args[i], shape);
        }
        return result;
    }

    // Deserialize each argument back into the declared parameter type of the
    // target method (which is what reflection Invoke requires). Missing trailing
    // arguments take the parameter's default value.
    private static object[] DeserializeArgs(byte[][] rawArgs, ParameterInfo[] parameters)
    {
        var result = new object[Math.Max(rawArgs.Length, parameters.Length)];
        for (int i = 0; i < result.Length; i++)
        {
            if (i >= rawArgs.Length)
            {
                result[i] = parameters[i].HasDefaultValue ? parameters[i].DefaultValue : null;
                continue;
            }
            if (rawArgs[i] is null)
            {
                result[i] = null;
                continue;
            }
            Type t = i < parameters.Length ? parameters[i].ParameterType : typeof(object);
            ITypeShape shape = ReflectionTypeShapeProvider.Default.GetTypeShape(t);
            result[i] = s.DeserializeObject(rawArgs[i], shape);
        }
        return result;
    }

    // ---- receiving --------------------------------------------------------

    private void OnMessageReceived(ulong from, Channel ch, byte[] msg)
    {
        if (ch != Channel.RPC_Main) return;
        RPC rpc = s.Deserialize<RPC>(msg);
        HandleRPC(from, rpc);
    }

    private void HandleRPC(ulong from, RPC rpc)
    {
        if (rpc.ObjectID != 0 && rpc.NodePath != null)
        {
            Logging.Error($"Malformed RPC {rpc.MethodName}: cannot have both node path and ObjectID!", "RPCManager");
            return;
        }

        Node target;
        if (rpc.ObjectID != 0)
        {
            GameWorld.syncedObjs.TryGetValue(rpc.ObjectID, out GMPObject gmpo);
            target = gmpo as Node;
        }
        else if (rpc.NodePath != null)
        {
            target = GetNodeOrNull(rpc.NodePath);
        }
        else
        {
            Logging.Error($"Malformed RPC {rpc.MethodName}: neither ObjectID nor NodePath specified!", "RPCManager");
            return;
        }

        if (target == null)
        {
            Logging.Warn($"RPC target not found: {(rpc.ObjectID != 0 ? $"ObjectID {rpc.ObjectID}" : $"NodePath {rpc.NodePath}")} (attempting to run {rpc.MethodName})", "RPCManager");
            return;
        }

        MethodInfo method = FindRpcMethod(target.GetType(), rpc.MethodName);
        if (method == null)
        {
            Logging.Error($"RPC {rpc.MethodName} from {from} rejected: no [RPC] method with that name on {target.GetType().Name}", "RPCManager");
            return;
        }

        if (method.GetCustomAttribute<RPCAttribute>().requireAuthority)
        {
            ulong expected = target is GMPObject g ? g.authority : Lobby.hostID;
            if (from != expected)
            {
                Logging.Warn($"RPC {rpc.MethodName} from {from} rejected: requires authority {expected}", "RPCManager");
                return;
            }
        }

        // Handlers often broadcast, and the local copy of that broadcast runs synchronously inside
        // this call, so restore the outer sender afterwards rather than clearing it.
        ulong outerSender = sender;
        sender = from;
        try
        {
            object[] callArgs = DeserializeArgs(rpc.args, method.GetParameters());
            method.Invoke(target, callArgs);
        }
        catch (TargetInvocationException e)
        {
            Logging.Error($"RPC {target.GetType().Name}.{rpc.MethodName} threw: {e.InnerException}", "RPCManager");
        }
        catch (Exception e) when (e is ArgumentException || e is TargetParameterCountException)
        {
            Logging.Error($"RPC {target.GetType().Name}.{rpc.MethodName} from {from} has mismatched arguments: {e.Message}", "RPCManager");
        }
        finally
        {
            sender = outerSender;
        }
    }

    // Walks the type hierarchy so private [RPC] methods declared on base classes are found too.
    private static MethodInfo FindRpcMethod(Type type, string name)
    {
        if (methodCache.TryGetValue((type, name), out MethodInfo cached))
            return cached;

        MethodInfo found = null;
        for (Type t = type; t != null && found == null; t = t.BaseType)
        {
            foreach (MethodInfo m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (m.Name == name && m.GetCustomAttribute<RPCAttribute>() != null)
                {
                    found = m;
                    break;
                }
            }
        }
        methodCache[(type, name)] = found;
        return found;
    }
}
