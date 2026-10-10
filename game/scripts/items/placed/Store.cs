using Godot;

public partial class Store : Structure
{
    /// <summary>Opens the shop screen for the interacting player (interact only runs on that player's own peer).</summary>
    public override void onInteract(ulong playerID)
    {
        if (GameWorld.syncedObjs.TryGetValue(playerID, out GMPObject obj) && obj is FactoryPlayer { isLocal: true } player)
        {
            UIManager.OpenShop(player.inventory);
        }
    }
}
