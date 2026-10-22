using Godot;

/// <summary>
/// The tool for taking structures apart. Hold left click on a structure for <see cref="deconstructTime"/> seconds and
/// the host removes it and gives its blueprint back (<see cref="BuildGrid.RequestDeconstruct"/>). Looking away or
/// letting go before then cancels it. While it's equipped, the hover text on a structure says how far along it is.
/// <para>
/// Multiplayer: nothing to sync. Only the holder's peer reads input and counts, and the host decides the deconstruct, so
/// two players finishing on the same structure at once give back one blueprint.
/// </para>
/// </summary>
public partial class Wrench : HeldItem
{
    /// <summary>How long (s) left click has to be held on a structure to take it apart.</summary>
    [Export] public float deconstructTime = 0.75f;

    // The holder's peer only.
    bool holding;
    /// <summary>The structure being worked on, and for how long (s) so far.</summary>
    Structure target;
    float heldFor;

    public override bool HandleInput(InputEvent @event)
    {
        if (@event.IsActionPressed(InputActions.primary))
        {
            holding = true;
            return true;
        }
        // Letting go is noticed in _PhysicsProcess, which also covers losing focus mid-hold.
        return @event.IsActionReleased(InputActions.primary);
    }

    // Runs after the player's own _PhysicsProcess (it's under the player in the tree), so the pick target and hover
    // text are this tick's, and the hover text set here replaces the structure's own.
    public override void _PhysicsProcess(double delta)
    {
        if (!isLocal) return;
        if (holding && !Input.IsActionPressed(InputActions.primary))
        {
            holding = false;
        }

        Structure looked = player.pickTarget as Structure;
        if (looked == null || !looked.canBeDeconstructed)
        {
            target = null;
            heldFor = 0;
            return;
        }

        // TODO(human): count up while left click is held on `looked`, and deconstruct it once heldFor reaches
        // deconstructTime (call Deconstruct(looked)). Decide what resets the count: letting go, looking at another structure.

        if (target == looked)
        {
            UIManager.SetHoverInfo(looked.hoverName, $"Deconstructing... {Mathf.RoundToInt(heldFor / deconstructTime * 100)}%");
        }
        else
        {
            UIManager.SetHoverInfo(looked.hoverName, "Hold left click to deconstruct");
        }
    }

    /// <summary>Asks the host to take <paramref name="structure"/> apart, if its blueprint fits in the inventory.</summary>
    void Deconstruct(Structure structure)
    {
        if (!player.inventory.HasRoomFor(structure.blueprintItemID))
        {
            Logging.Log($"No room in the inventory for {structure.blueprintItemID}: {structure.Name} isn't deconstructed", "Wrench");
            return;
        }
        BuildGrid.RequestDeconstruct(player, structure);
    }
}
