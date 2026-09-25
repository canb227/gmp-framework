using Godot;
using Godot.Collections;

/// <summary>
/// FactoryPlayer: looks-at targeting. Raycasts from the camera each physics tick to find the item or
/// button or structure under the crosshair, shows the hover labels, and handles the "interact" action
/// (pick up an item / press a button / deconstruct a structure).
/// </summary>
public partial class FactoryPlayer
{
    [Export] public float pickRange = 5f;

    /// <summary>The PhysicalFactoryItem, BasicButton or Structure currently under the crosshair, or null.</summary>
    public Node3D pickTarget { get; private set; }

    Label hoverInfoName;
    Label hoverInfoBelow;

    void ReadyInteraction()
    {
        hoverInfoName = hud.GetNode<Label>("%HoverInfoName");
        hoverInfoBelow = hud.GetNode<Label>("%HoverInfoBelow");
    }

    void HandleInteractionInput(InputEvent @event)
    {
        if (!@event.IsActionPressed("interact")) return;

        if (pickTarget is PhysicalFactoryItem item)
        {
            Logging.Log($"You just pressed interact on {item.Name}!", "Player");
            if (item.canBePickedUp && inventory.HasRoomFor(item.itemID))
            {
                // Arbitrated by the item's authority so two players can't both pick it up.
                item.RequestPickup(this);
            }
        }
        if (pickTarget is BasicButton button)
        {
            Logging.Log($"You just pressed interact on {button.Name}!", "Player");
            button.OnPressed();
        }
        if (pickTarget is Structure structure && inventory.HasRoomFor(structure.blueprintItemID))
        {
            // Arbitrated by the host so two players can't both take the blueprint back.
            BuildGrid.RequestDeconstruct(this, structure);
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
        Dictionary<string, Variant> ray = GameWorld.Raycast(camera.GlobalPosition, camera.GlobalPosition + -camera.GlobalTransform.Basis.Z * pickRange);
        Node hit = ray["hit"].AsBool() ? (Node)ray["collider"].AsGodotObject() : null;

        if (hit is PhysicalFactoryItem item)
        {
            pickTarget = item;
            hoverInfoName.Show();
            // Items without a definition resource (e.g. test props) fall back to their id.
            hoverInfoName.Text = FactoryItem.Fetch(item.itemID)?.displayName ?? item.itemID;
            if (item.canBePickedUp)
            {
                hoverInfoBelow.Show();
                hoverInfoBelow.Text = "Press F to pickup!";
            }
            else
            {
                hoverInfoBelow.Hide();
            }
        }
        else if (hit is BasicButton button)
        {
            pickTarget = button;
            hoverInfoName.Hide();
            hoverInfoBelow.Show();
            hoverInfoBelow.Text = "Press F to Activate.";
        }
        else if (BuildGrid.FindStructure(hit) is Structure structure)
        {
            pickTarget = structure;
            hoverInfoName.Show();
            hoverInfoName.Text = structure.displayName;
            hoverInfoBelow.Show();
            hoverInfoBelow.Text = "Press F to deconstruct.";
        }
        else
        {
            pickTarget = null;
            hoverInfoName.Hide();
            hoverInfoBelow.Hide();
        }
    }
}
