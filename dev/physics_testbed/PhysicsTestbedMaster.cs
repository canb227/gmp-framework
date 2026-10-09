using Godot;
using System.Linq;

/// <summary>
/// Physics-testbed master controls: every segment's spawner lever on or off, and deleting every
/// <see cref="deleteItemID"/> item in the world. Decided by the host, like the levers themselves.
/// </summary>
public partial class PhysicsTestbedMaster : Node3D
{
    [Export] public string deleteItemID = "scrap_ball";

    public void RequestAllSpawners(bool on)
    {
        RPCManager.RPCTo(Lobby.hostID, this, nameof(_RequestAllSpawners), [on]);
    }

    public void RequestDeleteAll()
    {
        RPCManager.RPCTo(Lobby.hostID, this, nameof(_RequestDeleteAll), []);
    }

    [RPC]
    private void _RequestAllSpawners(bool on)
    {
        foreach (PhysicsTuningStation station in PhysicsTuningStation.all)
        {
            ((Activator)station.lever)?.SetFromAuthority(on);
        }
    }

    [RPC]
    private void _RequestDeleteAll()
    {
        ulong[] ids = GameWorld.syncedObjs.Values
            .Where(g => g is PhysicalFactoryItem item && item.itemID == deleteItemID)
            .Select(g => g.id).ToArray();
        foreach (ulong id in ids)
        {
            GameWorld.DespawnObject(id);
        }
    }
}
