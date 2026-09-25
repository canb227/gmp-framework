using Godot;
using PolyType;

/// <summary>Position + Euler rotation snapshot sent by the authority for transform-synced objects.</summary>
[GenerateShape]
public partial record struct TransformSyncState
{
    public Vector3 pos;
    public Vector3 rot;
}

/// <summary>
/// Shared StateUpdate helpers for GMPO node types. Godot nodes can't share a base class across
/// Node3D/Box3D wrappers, so common sync logic lives here instead.
/// </summary>
public static class SyncHelpers
{
    /// <summary>Serializes the node's local position and rotation.</summary>
    public static byte[] WriteTransform(Node3D node)
    {
        return GMPObject.serializer.Serialize(new TransformSyncState { pos = node.Position, rot = node.Rotation });
    }

    /// <summary>Deserializes a transform state; returns false if there is no state yet.</summary>
    public static bool TryReadTransform(byte[] state, out TransformSyncState s)
    {
        if (state == null || state.Length == 0)
        {
            s = default;
            return false;
        }
        s = GMPObject.serializer.Deserialize<TransformSyncState>(state);
        return true;
    }

    /// <summary>
    /// Frame-rate-independent smoothing weight: the fraction of the remaining distance to cover this frame
    /// when approaching a target at <paramref name="rate"/> (1/s). A rate of 0 or less means snap (weight 1).
    /// </summary>
    public static float LerpWeight(float rate, double delta)
    {
        return rate <= 0f ? 1f : 1f - Mathf.Exp(-rate * (float)delta);
    }

    /// <summary>
    /// Moves the node toward <paramref name="target"/> by <paramref name="weight"/> (see <see cref="LerpWeight"/>);
    /// 1 or more snaps. Rotation is slerped so it takes the short way around instead of spinning at ±180°.
    /// </summary>
    public static void LerpTransform(Node3D node, TransformSyncState target, float weight)
    {
        if (weight >= 1f)
        {
            node.Position = target.pos;
            node.Rotation = target.rot;
            return;
        }
        node.Position = node.Position.Lerp(target.pos, weight);
        node.Quaternion = node.Quaternion.Slerp(Quaternion.FromEuler(target.rot), weight);
    }
}
