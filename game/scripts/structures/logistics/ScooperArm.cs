using Godot;


public partial class ScooperArm : Structure
{

    [Export]
    public Node3D ikTarget;


    [Export]
    public Node3D bucketTarget;


    [Export]
    public Godot.Collections.Array<Node3D> waypoints = new();

    [Export]
    public float secondsPerWaypoint = 1f;
    const float BucketReach = 1f;

    public override void _PhysicsProcess(double delta)
    {
        if (waypoints.Count < 2)
        {
            return;
        }
        int count = waypoints.Count;

        //dark claude animation technology
        double loop = GameWorld.netTime / 1000.0 / secondsPerWaypoint;
        int from = (int)(loop % count);
        float t = (float)(loop % 1.0);
        Transform3D before = waypoints[(from + count - 1) % count].GlobalTransform;
        Transform3D a = waypoints[from].GlobalTransform;
        Transform3D b = waypoints[(from + 1) % count].GlobalTransform;
        Transform3D after = waypoints[(from + 2) % count].GlobalTransform;
        ikTarget.GlobalPosition = a.Origin.CubicInterpolate(b.Origin, before.Origin, after.Origin, t);
        Vector3 down = Vector3.Down * BucketReach;
        bucketTarget.GlobalPosition = (a * down).CubicInterpolate(b * down, before * down, after * down, t);
    }
}
