using Godot;

/// <summary>
/// FactoryPlayer: looks-at targeting. Raycasts from the camera each physics tick to find the item,
/// activator (button, lever) or structure under the crosshair, shows the HUD hover labels, and handles the "interact" action
/// (pick up an item / press a button / flip a lever / open the shop), and taking structures apart by holding X, or
/// left click with the <see cref="Wrench"/>.
/// </summary>
public partial class FactoryPlayer
{
    [Export] public float pickRange = 5f;

    /// <summary>How long (s) X (or left click with the wrench) has to be held on a structure to take it apart.</summary>
    [Export] public float deconstructTime = 0.75f;

    /// <summary>The structure being taken apart, and for how long (s) so far. Local player only.</summary>
    Structure deconstructTarget;
    float deconstructHeldFor;

    /// <summary>The body (activator, structure, dropped tool...) currently under the crosshair, or null.</summary>
    public GMPOBody pickTarget { get; private set; }

    /// <summary>The loose item currently under the crosshair, or null (then <see cref="pickTarget"/> may be set).</summary>
    public ItemRecord pickItem { get; private set; }

    void HandleInteractionInput(InputEvent @event)
    {
        if (!@event.IsActionPressed(InputActions.interact)) return;

        if (pickItem != null)
        {
            ItemWorld.Interact(pickItem, this);
            return;
        }
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
        RayHit ray = PhysicsWorld.Raycast(camera.GlobalPosition, camera.GlobalPosition + -camera.GlobalTransform.Basis.Z * pickRange);
        pickItem = null;
        if (ray.hit)
        {
            ItemRecord item = ItemWorld.Get(ray.id);
            if (item != null && item.canBePickTarget)
            {
                pickItem = item;
                pickTarget = null;
                UIManager.SetHoverInfo(item.hoverName, item.hoverText);
                return;
            }
        }
        // A hit on a structure's belt line or plain child body resolves to the structure.
        GMPOBody target = PhysicsWorld.BodyNode(ray.id) as GMPOBody;
        if (target is not Interactable && string.IsNullOrEmpty(target?.hoverName) && string.IsNullOrEmpty(target?.hoverText))
        {
            target = BuildGrid.FindStructure(ray);
        }
        pickTarget = target;
        UIManager.SetHoverInfo(target?.hoverName, target?.hoverText);
    }

    /// <summary>
    /// Counts while X (or left click with the wrench) is held on the structure under the crosshair, and asks the host to
    /// take it apart once it's been held <see cref="deconstructTime"/>. Letting go or looking at something else starts
    /// over. Runs each physics tick on the local player, after <see cref="UpdatePickTarget"/>, whose hover text it replaces.
    /// </summary>
    void UpdateDeconstruct(float delta)
    {
        bool wrench = heldItem is Wrench;
        bool holding = false;
        if (!UIManager.BlocksGameplayInput)
        {
            holding = Input.IsActionPressed(InputActions.deconstruct) || (wrench && Input.IsActionPressed(InputActions.primary));
        }

        Structure looked = pickTarget as Structure;
        if (looked == null || !looked.canBeDeconstructed)
        {
            deconstructTarget = null;
            deconstructHeldFor = 0;
            return;
        }

        if (!holding || looked != deconstructTarget)
        {
            deconstructHeldFor = 0;
        }
        deconstructTarget = null;
        if (holding)
        {
            deconstructTarget = looked;
            deconstructHeldFor += delta;
            if (deconstructHeldFor >= deconstructTime)
            {
                // The host takes a round trip to remove it; starting over keeps it from being asked again meanwhile.
                deconstructHeldFor = 0;
                Deconstruct(looked);
            }
        }

        if (deconstructTarget != null)
        {
            UIManager.SetHoverInfo(looked.hoverName, $"Deconstructing... {Mathf.RoundToInt(deconstructHeldFor / deconstructTime * 100)}%");
        }
        else if (wrench)
        {
            UIManager.SetHoverInfo(looked.hoverName, "Hold left click or X to deconstruct");
        }
    }

    /// <summary>Asks the host to take <paramref name="structure"/> apart, if its blueprint fits in the inventory.</summary>
    void Deconstruct(Structure structure)
    {
        if (!inventory.HasRoomFor(structure.blueprintItemID))
        {
            Logging.Log($"No room in the inventory for {structure.blueprintItemID}: {structure.Name} isn't deconstructed", "Player");
            return;
        }
        BuildGrid.RequestDeconstruct(this, structure);
    }
}
