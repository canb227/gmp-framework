using Godot;
using Nerdbank.MessagePack;
using PolyType;
using PolyType.ReflectionProvider;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

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

public partial class RPCManager : Node
{
    public static MessagePackSerializer s;
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

    public static Error RPC(Node targetNode, string methodName, object[] args)
    {
        RPC rpc = new();
        rpc.MethodName = methodName;
        rpc.NodePath = targetNode.GetPath();
        rpc.args = SerializeArgs(args);
        return Lobby.SendToAllAndSelf(Channel.RPC_Main, s.Serialize(rpc));
    }

    public static Error RPC(ulong objectID, string methodName, object[] args)
    {
        RPC rpc = new();
        rpc.MethodName = methodName;
        rpc.ObjectID = objectID;
        rpc.args = SerializeArgs(args);
        return Lobby.SendToAllAndSelf(Channel.RPC_Main,s.Serialize(rpc));
    }

    public static Error RPC(string nodePath, string methodName, object[] args)
    {
        //Logging.Log($"Sending RPC to {nodePath}: {methodName} , args: {string.Join(", ", args)}", "RPCManager");
        RPC rpc = new();
        rpc.MethodName = methodName;
        rpc.NodePath = nodePath;
        rpc.args = SerializeArgs(args);
        return Lobby.SendToAllAndSelf(Channel.RPC_Main, s.Serialize(rpc),Network.k_nSteamNetworkingSend_Reliable);
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
    // target method (which is what reflection Invoke requires).
    private static object[] DeserializeArgs(byte[][] rawArgs, ParameterInfo[] parameters)
    {
        var result = new object[rawArgs.Length];
        for (int i = 0; i < rawArgs.Length; i++)
        {
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

    private void OnMessageReceived(ulong from, Channel ch, byte[] msg)
    {
        if (ch == Channel.RPC_Main)
        {
            RPC rpc = s.Deserialize<RPC>(msg);
            HandleRPC(from,rpc);
        }
        else if (false)
        {

        }
        else
        {
            return;
        }
    }

    private void HandleRPC(ulong from, RPC rpc)
    {
        if (rpc.ObjectID != 0 && rpc.NodePath != null)
        {
            Logging.Error("Malformed RPC cannot have both node path and ObjectID!", "RPCManager");
        }

       // Logging.Log($"Got an RPC from {from}: {rpc.NodePath}.{rpc.MethodName} , {rpc.args.Length} arg(s)", "RPCManager");
        if (rpc.ObjectID != 0)
        {
            if (GameWorld.syncedObjs.TryGetValue(rpc.ObjectID, out GMPObject gmpo))
            {
                Node node = gmpo as Node;
                MethodInfo method = node.GetType().GetMethod(rpc.MethodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                object[] callArgs = DeserializeArgs(rpc.args, method.GetParameters());
                method.Invoke(node, callArgs);
            }
            else
            {
                Logging.Warn($"RPC target not found: ObjectID {rpc.ObjectID}", "RPCManager");
            }
        }
        else if (rpc.NodePath != null)
        {
            Node? targetNode = GetNodeOrNull(rpc.NodePath);
            if (targetNode != null)
            {
                MethodInfo method = targetNode.GetType().GetMethod(rpc.MethodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                object[] callArgs = DeserializeArgs(rpc.args, method.GetParameters());
                method.Invoke(targetNode, callArgs);
            }
            else
            {
                Logging.Warn($"RPC target not found: NodePath {rpc.NodePath} (attempting to run {rpc.MethodName})", "RPCManager");
            }
        }
        else
        {
            Logging.Error("Malformed RPC: neither ObjectID nor NodePath specified!", "RPCManager");
        }
    }
}

