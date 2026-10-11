using Godot;

/// <summary>
/// A floor conveyor that shapes itself to the belts feeding it. It always carries items out through its front; what
/// changes is where they get on. Its scene holds one form per <see cref="Shape"/>: the straight, fed from behind, and
/// the two turns, fed from the left or right side. Each form is a static sub-body with its own model, belt and walls,
/// and only the current one is enabled and visible: Box3D ignores shape nodes
/// added or removed at runtime, so a whole sub-body is switched instead.
/// <para>
/// The build grid picks the form (BuildGrid.BeltShapes.cs) from the output ports that meet its input ports: fed from
/// behind, it stays straight; otherwise, fed from exactly one side, it turns from that side; otherwise (nothing
/// feeding it, or both sides) it stays straight. Every form lets items out at the same place, so changing form never
/// changes what a conveyor feeds, and the forms depend only on which structures are built. Each peer works them out
/// from its own grid and they agree, as the belt lines do; nothing about them is synced.
/// </para>
/// </summary>
public partial class ConveyorStructure : Structure
{
    /// <summary>The conveyor's forms, by the face items get on through. The order matches the scene's variants.</summary>
    public enum Shape { Straight, TurnFromLeft, TurnFromRight }

    private static readonly NodePath[] VariantPaths = ["Straight", "TurnFromLeft", "TurnFromRight"];

    /// <summary>The form currently enabled.</summary>
    public Shape shape { get; private set; } = Shape.Straight;

    private Node3D[] variants;
    private ConveyorBelt[] belts;
    // Each form's input: its belt's start, as a port in the root's frame.
    private PortShape[] inputs;

    /// <summary>The belt of the current form: the only one that joins a belt line.</summary>
    public ConveyorBelt activeBelt
    {
        get
        {
            FindVariants();
            return belts[(int)shape];
        }
    }

    /// <summary>Where <paramref name="form"/> takes items on: the port at its belt's start, in the root's frame.</summary>
    public PortShape InputPort(Shape form)
    {
        FindVariants();
        return inputs[(int)form];
    }

    /// <summary>
    /// Enables <paramref name="form"/> and disables the others. Only the node state: on a built conveyor, go through
    /// <see cref="BuildGrid"/>, which also swaps its belt in the belt lines.
    /// </summary>
    public void ApplyShape(Shape form)
    {
        FindVariants();
        shape = form;
        RefreshPorts();
        for (int i = 0; i < variants.Length; i++)
        {
            bool on = i == (int)form;
            variants[i].Set(Box3DNames.enabled, on);
            variants[i].Visible = on;
        }
    }

    // Looked up once, on first use: placement probes use a conveyor before (or without) it entering the tree.
    private void FindVariants()
    {
        if (variants != null)
        {
            return;
        }
        int n = VariantPaths.Length;
        variants = new Node3D[n];
        belts = new ConveyorBelt[n];
        inputs = new PortShape[n];
        for (int i = 0; i < n; i++)
        {
            variants[i] = GetNode<Node3D>(VariantPaths[i]);
            foreach (Node child in variants[i].GetChildren())
            {
                if (child is ConveyorBelt belt && belt.points.Length >= 2)
                {
                    belts[i] = belt;
                    break;
                }
            }
            ConveyorBelt b = belts[i];
            Transform3D rel = variants[i].Transform * b.Transform;
            inputs[i] = StructurePorts.FromBeltEnd(rel * b.points[0], rel.Basis * (b.points[1] - b.points[0]),
                rel.Basis * b.normal, b.width, false);
        }
    }
}
