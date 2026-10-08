using Godot;
using System;
using System.Collections.Generic;

public partial class Store : Structure
{


    List<ItemInfo> itemsForSale = new List<ItemInfo>();

    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
    }

    // Called every frame. 'delta' is the elapsed time since the previous frame.
    public override void _Process(double delta)
    {
    }

    /// <summary>Opens the shop screen for the interacting player (interact only runs on that player's own peer).</summary>
    public override void onInteract(ulong playerID)
    {
        if (GameWorld.syncedObjs.TryGetValue(playerID, out GMPObject obj) && obj is FactoryPlayer { isLocal: true } player)
        {
            UIManager.OpenShop(player.inventory);
        }
    }
}
