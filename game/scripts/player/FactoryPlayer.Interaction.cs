using Godot;

/// <summary>
/// FactoryPlayer: looks-at targeting. Raycasts from the camera each physics tick to find the item,
/// activator (button, lever) or structure under the crosshair, shows the HUD hover labels, and handles the "interact" action
/// (pick up an item / press a button / flip a lever / deconstruct a structure).
/// </summary>
public partial class FactoryPlayer
{
    [Export] public float pickRange = 5f;

    /// <summary>The body (item, activator, structure...) currently under the crosshair, or null.</summary>
    public GMPOBox3DBody pickTarget { get; private set; }

    void HandleInteractionInput(InputEvent @event)
    {
        if (!@event.IsActionPressed(InputActions.interact)) return;

        if (pickTarget is Interactable i)
        {
            Logging.Log($"You just pressed interact on {pickTarget.Name}!", "Player");
            i.onInteract(id);
        }
    }

    /// <summary>Adds an item granted to this (local) player by an arbiter (a pickup, refund or deconstruct).</summary>
    public void ReceiveItem(string itemID)
    {
        int leftover = inventory.AddItem(itemID, 1);
        if (leftover > 0)
        {
            Logging.Warn($"Received {itemID} but the inventory filled up in the meantime; item lost", "Player");
        }
        UpdateEquippedItem();
    }

    /// <summary>Refreshes <see cref="pickTarget"/> and the hover labels. Runs each physics tick on the local player.</summary>
    void UpdatePickTarget()
    {
        RayHit ray = GameWorld.Raycast(camera.GlobalPosition, camera.GlobalPosition + -camera.GlobalTransform.Basis.Z * pickRange);
        // A hit on a structure's belt line or plain child body resolves to the structure.
        GMPOBox3DBody target = ray.collider as GMPOBox3DBody;
        if (target is not Interactable && string.IsNullOrEmpty(target?.hoverName) && string.IsNullOrEmpty(target?.hoverText))
        {
            target = BuildGrid.FindStructure(ray);
        }
        pickTarget = target;
        UIManager.SetHoverInfo(target?.hoverName, target?.hoverText);
    }
}
