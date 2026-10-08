using Godot;

/// <summary>
/// FactoryPlayer: looks-at targeting. Raycasts from the camera each physics tick to find the item or
/// button, lever or structure under the crosshair, shows the HUD hover labels, and handles the "interact" action
/// (pick up an item / press a button / flip a lever / deconstruct a structure).
/// </summary>
public partial class FactoryPlayer
{
    [Export] public float pickRange = 5f;

    /// <summary>The PhysicalFactoryItem, BasicButton, Lever or Structure currently under the crosshair, or null.</summary>
    public Node3D pickTarget { get; private set; }

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
        Node hit = ray.collider;

        string name = null, prompt = null;
        if (hit is PhysicalFactoryItem item)
        {
            pickTarget = item;
            // Items without a definition resource (e.g. test props) fall back to their id.
            name = ItemInfo.Fetch(item.itemID)?.displayName ?? item.itemID;
            if (item.canBePickedUp) prompt = "Press F to " + item.interactionText;
        }
        else if (hit is BasicButton button)
        {
            pickTarget = button;
            prompt = "Press F to Activate.";
        }
        else if (hit is Lever lever)
        {
            pickTarget = lever;
            name = lever.displayName;
            prompt = lever.pulled ? "Press F to push." : "Press F to pull.";
        }
        else if (hit is TestbedButton testbedButton)
        {
            pickTarget = testbedButton;
            name = testbedButton.displayName;
            prompt = testbedButton.prompt;
        }
        else if (BuildGrid.FindStructure(ray) is Structure structure)
        {
            pickTarget = structure;
            name = structure.displayName;
            prompt = "Press F to " + structure.interactionText;
        }
        else
        {
            pickTarget = null;
        }
        UIManager.SetHoverInfo(name, prompt);
    }
}
