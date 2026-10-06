using Godot;
using Godot.Collections;

/// <summary>
/// FactoryPlayer: looks-at targeting. Raycasts from the camera each physics tick to find the item or
/// button, lever or structure under the crosshair, shows the hover labels, and handles the "interact" action
/// (pick up an item / press a button / flip a lever / deconstruct a structure).
/// </summary>
public partial class FactoryPlayer
{
    [Export] public float pickRange = 5f;

    /// <summary>The PhysicalFactoryItem, BasicButton, Lever or Structure currently under the crosshair, or null.</summary>
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

        if (hit is PhysicalFactoryItem item)
        {
            pickTarget = item;
            hoverInfoName.Show();
            // Items without a definition resource (e.g. test props) fall back to their id.
            hoverInfoName.Text = ItemInfo.Fetch(item.itemID)?.displayName ?? item.itemID;
            if (item.canBePickedUp)
            {
                hoverInfoBelow.Show();
                hoverInfoBelow.Text = "Press F to " + item.interactionText;
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
        else if (hit is Lever lever)
        {
            pickTarget = lever;
            hoverInfoName.Show();
            hoverInfoName.Text = lever.displayName;
            hoverInfoBelow.Show();
            hoverInfoBelow.Text = lever.pulled ? "Press F to push." : "Press F to pull.";
        }
        else if (hit is TestbedButton testbedButton)
        {
            pickTarget = testbedButton;
            hoverInfoName.Show();
            hoverInfoName.Text = testbedButton.displayName;
            hoverInfoBelow.Show();
            hoverInfoBelow.Text = testbedButton.prompt;
        }
        else if (BuildGrid.FindStructure(ray) is Structure structure)
        {
            pickTarget = structure;
            hoverInfoName.Show();
            hoverInfoName.Text = structure.displayName;
            hoverInfoBelow.Show();
            hoverInfoBelow.Text = "Press F to " + structure.interactionText;
        }
        else
        {
            pickTarget = null;
            hoverInfoName.Hide();
            hoverInfoBelow.Hide();
        }
    }
}
