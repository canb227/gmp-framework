using Godot;
using System.Collections.Generic;

/// <summary>
/// BuildGrid: the forms of the self-turning floor conveyors (<see cref="ConveyorStructure"/>). A conveyor carries
/// items out through its front and takes them on through whichever face is fed, Factorio style:
/// <list type="bullet">
/// <item>an output port connects to its back input: straight (feeding from behind wins);</item>
/// <item>otherwise one connects to exactly one side input: a turn from that side;</item>
/// <item>otherwise (nothing feeds it, or both sides do, which can't pick a way to turn): straight.</item>
/// </list>
/// Any output port counts as a feeder (BuildGrid.Ports.cs): another conveyor's, a slope's, a machine's mouth.
/// <para>
/// No form moves a conveyor's output, and its ports are registered for every form at once, so a conveyor's form
/// never changes what another is fed by: the forms are a function of the built structures alone, whatever order they
/// were registered in. A change to the ports on a face plane only reshapes the conveyors fed there, and nothing beyond
/// them. Reshaping runs as structures register, before the belt lines are rebuilt in the next <c>_PhysicsProcess</c>,
/// so lines are always built from settled forms; every peer gets the same forms from its own grid, as with the lines.
/// </para>
/// </summary>
public partial class BuildGrid
{
    /// <summary>
    /// The form <paramref name="conveyor"/> takes with its root at <paramref name="pose"/>, from the ports built around
    /// it. Usable on a placement preview, which is never registered (it previews only its own form).
    /// </summary>
    public static ConveyorStructure.Shape ResolveConveyorShape(ConveyorStructure conveyor, Transform3D pose)
    {
        if (IsFed(conveyor, pose, ConveyorStructure.Shape.Straight))
        {
            return ConveyorStructure.Shape.Straight;
        }
        bool left = IsFed(conveyor, pose, ConveyorStructure.Shape.TurnFromLeft);
        bool right = IsFed(conveyor, pose, ConveyorStructure.Shape.TurnFromRight);
        if (left == right)
        {
            return ConveyorStructure.Shape.Straight;
        }
        return left ? ConveyorStructure.Shape.TurnFromLeft : ConveyorStructure.Shape.TurnFromRight;
    }

    // Whether another structure's output port connects to the input `form` takes items on through.
    private static bool IsFed(ConveyorStructure conveyor, Transform3D pose, ConveyorStructure.Shape form)
    {
        WorldPort input = ToWorld(conveyor, conveyor.InputPort(form), pose);
        if (!portsByPlane.TryGetValue(PlaneKey(input), out List<WorldPort> ports))
        {
            return false;
        }
        foreach (WorldPort other in ports)
        {
            if (other.owner != conveyor && Connects(other, input))
            {
                return true;
            }
        }
        return false;
    }

    // An output port was built or removed: reshape the registered conveyors it connects to.
    private static void ReshapeConveyorsFedBy(in WorldPort output)
    {
        if (!portsByPlane.TryGetValue(PlaneKey(output), out List<WorldPort> ports))
        {
            return;
        }
        // Reshaping swaps belts, which never touches the port lists, so this one can be walked as is.
        foreach (WorldPort input in ports)
        {
            if (input.owner is ConveyorStructure conveyor && conveyor != output.owner && Connects(output, input))
            {
                ReshapeConveyor(conveyor);
            }
        }
    }

    private static void ReshapeConveyor(ConveyorStructure conveyor)
    {
        ConveyorStructure.Shape form = ResolveConveyorShape(conveyor, conveyor.GlobalTransform);
        if (form == conveyor.shape)
        {
            return;
        }
        // Swap its belt in the lines without touching its ports: they cover every form already.
        bool registered = RemoveBelts(conveyor) != null;
        SetConveyorShape(conveyor, form);
        if (registered) AddBelts(conveyor);
    }

    private static void SetConveyorShape(ConveyorStructure conveyor, ConveyorStructure.Shape form)
    {
        if (form == conveyor.shape)
        {
            return;
        }
        // Rare (a belt built or removed beside it), and each peer should log the same, so it's easy to compare them.
        Logging.Log($"{conveyor.Name} at {conveyor.anchor} is now {form}", "BeltShapes");
        conveyor.ApplyShape(form);
    }
}
