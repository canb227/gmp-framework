using Godot;

/// <summary>
/// A hand-placed box in a level that stops structures being built in any grid cell it touches
/// (<see cref="BuildGrid.CanPlace"/>). Extends <see cref="CsgBox3D"/> for its editor face handles: drag a face to
/// resize from that side, hold Alt to resize symmetrically. Shown only in the editor; in game it is hidden,
/// has no collision and only registers its bounds with <see cref="BuildGrid"/>.
/// </summary>
[Tool]
[GlobalClass]
public partial class NoBuildVolume : CsgBox3D
{
    static readonly Color EditorColor = new(1.0f, 0.15f, 0.1f, 0.25f);

    public override void _EnterTree()
    {
        if (Engine.IsEditorHint())
        {
            if (Material == null)
            {
                Material = new StandardMaterial3D
                {
                    AlbedoColor = EditorColor,
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                };
            }
            return;
        }
        Visible = false;
        UseCollision = false;
        BuildGrid.noBuildVolumes.Add(this);
    }

    public override void _ExitTree()
    {
        BuildGrid.noBuildVolumes.Remove(this);
    }

    /// <summary>World-space bounds. A rotated volume blocks its enclosing axis-aligned box.</summary>
    public Aabb WorldBox => GlobalTransform * new Aabb(-Size / 2, Size);
}
