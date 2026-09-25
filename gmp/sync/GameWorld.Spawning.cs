using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// GameWorld: scene spawning and despawning. Owns <see cref="SpawnScene"/>/<see cref="DespawnObject"/>
/// and their RPC-target internals, GMPO init-data generation for a spawned node and its GMPObject
/// children, and the synced-object registry (<see cref="syncedObjs"/>).
/// </summary>
public partial class GameWorld
{
    public static Dictionary<ulong, GMPObject> syncedObjs = new();

    /// <summary>Every root node spawned this session (synced or not, e.g. the level), freed by <see cref="ResetSession"/>.</summary>
    private static readonly List<Node> spawnedRoots = new();

    private static GMPOInitData initDataNormalizer(GMPOInitData initData)
    {

        if (initData.id == 0)
        {
            initData.id = GenRandomID();
        }
        if (initData.authority == 0)
        {
            initData.authority = Lobby.selfPeerID;
        }
        if (initData.owner == 0)
        {
            initData.owner = Lobby.selfPeerID;
        }
        return initData;
    }

    private static Dictionary<string,GMPOInitData> childGMPORegisterGenerator(Node node, GMPOInitData initData)
    {
        Dictionary<string, GMPOInitData> childInit = new();
        foreach (Node c in node.FindChildren("*").ToList())
        {
            if (c is GMPObject gmpoChild)
            {
                childInit[node.GetPathTo(c)] = new GMPOInitData(
                    GenRandomID(), initData.authority, initData.owner,
                    gmpoChild.priority, gmpoChild.pauseable);
            }
        }
        return childInit;
    }

    /// <summary>
    /// Spawns a scene for every peer via RPC, including the caller. The RPC target running on this
    /// peer is invoked synchronously as part of this call, so by the time <c>SpawnScene</c> returns,
    /// <see cref="syncedObjs"/>[returned id] is already populated with the local copy.
    /// </summary>
    public static ulong SpawnScene(string scenePath, Vector3 position = default, Vector3 rotation = default, GMPOInitData initData = new(), byte[] initState = null, NodePath parent = null)
    {
        return SpawnScene(ResourceLoader.Load<PackedScene>(scenePath), position, rotation, initData, initState,parent);
    }

    /// <summary>Overload of <see cref="SpawnScene(string, Vector3, Vector3, GMPOInitData, byte[], NodePath)"/> taking an already-loaded <see cref="PackedScene"/>.</summary>
    public static ulong SpawnScene(PackedScene pck, Vector3 position = default, Vector3 rotation = default, GMPOInitData initData = new(), byte[] initState = null, NodePath parent = null)
    {
        Node node = pck.Instantiate();
        initData = initDataNormalizer(initData);
        if (node is GMPObject rootGmpo)
        {
            // Carry the scene's own "pauseable" setting (Init applies it on every peer).
            initData.pauseable |= rootGmpo.pauseable;
        }
        Dictionary<string, GMPOInitData> childInit = childGMPORegisterGenerator(node, initData);
        node.Free();
        RPCManager.RPC(instance, nameof(_SpawnScene), [pck.ResourcePath, position, rotation, initData, initState, childInit,parent]);
        return initData.id;
    }



    [RPC]
    private void _SpawnScene(string scenePath, Vector3 position, Vector3 rotation, GMPOInitData initData, byte[] initState = null, Dictionary<string, GMPOInitData> childInit = null, NodePath parent = null)
    {
        Node node = ResourceLoader.Load<PackedScene>(scenePath).Instantiate();
        Node parentNode = null;
        if (parent != null && parent != "")
        {
            parentNode = GetNode(parent);
        }

        _SpawnInternal(node, position, rotation, initData, initState, childInit, parentNode);
    }

    private void _SpawnPackedScene(PackedScene pck, Vector3 position, Vector3 rotation, GMPOInitData initData, byte[] initState = null, Dictionary<string, GMPOInitData> childInit = null, NodePath parent = null)
    {
        Node node = pck.Instantiate();
                Node parentNode = null;
        if (parent != null && parent!="")
        {
            parentNode = GetNode(parent);
        }
        _SpawnInternal(node, position, rotation, initData, initState, childInit, parentNode);
    }

    private void _SpawnInternal(Node node, Vector3 position, Vector3 rotation, GMPOInitData initData, byte[] initState = null, Dictionary<string, GMPOInitData> childInit = null, Node parent = null)
    {
        node.Name = node.GetType().ToString() + initData.id.ToString();

        if (b3droot == null)
        {
            Logging.Error("attempted to spawn with no b3droot!", "GameWorld");
        }
        else
        {
            spawnedRoots.Add(node);
            if (parent == null)
            {
                b3droot.AddChild(node);
            }
            else
            {
                parent.AddChild(node);
            }
            if (node is GMPOBox3DBody box)
            {
                box.Call("teleport", [new Transform3D(Basis.FromEuler(rotation), position)]);
            }
            else if (node is Node3D n)
            {
                n.Position = position;

                n.Rotation = rotation;

            }
        }



        if (node is GMPObject gmpo)
        {
            gmpo.Init(initData, initState);
            syncedObjs.Add(gmpo.id, gmpo);
        }
        else
        {
            // Normal for levels and other static scenery; their GMPObject children are still registered below.
            Logging.Log($"Spawned {node.Name} ({node.GetType()}), which is not a GMPObject; only its GMPObject children are synced.", "GameWorld");
        }
        if (node is Node3D n3d)
        {
            n3d.Position = position;
            n3d.Rotation = rotation;
        }
        if (childInit != null)
        {
            foreach (Node c in node.FindChildren("*").ToList())
            {
                if (c is not GMPObject gmpoChild)
                {
                    continue;
                }
                string rel = node.GetPathTo(c);
                if (!childInit.TryGetValue(rel, out GMPOInitData ci))
                {
                    Logging.Warn($"No init data for child GMPObject at '{rel}' under {node}; it will not be synced.", "GameWorld");
                    continue;
                }
                gmpoChild.Init(ci, null);
                syncedObjs.Add(gmpoChild.id, gmpoChild);
            }
        }

    }

    /// <summary>Despawns a previously-spawned synced object for every peer via RPC.</summary>
    public static void DespawnObject(ulong id)
    {
        RPCManager.RPC(instance, nameof(_DespawnObject), [id]);
    }

    [RPC]
    private void _DespawnObject(ulong id)
    {
        RemoveLocal(id);
    }

    /// <summary>
    /// Frees this peer's copy of a synced object without messaging anyone. For handlers that already
    /// run on every peer (e.g. an authority-broadcast outcome); otherwise use <see cref="DespawnObject"/>.
    /// </summary>
    public static void RemoveLocal(ulong id)
    {
        if (syncedObjs.TryGetValue(id, out GMPObject gmpo))
        {
            syncedObjs.Remove(id);
            heldBy.Remove(id);
            (gmpo as Node)?.QueueFree();
        }
    }

    private static ulong GenRandomID()
    {
        return (ulong)Random.Shared.NextInt64();
    }
}
