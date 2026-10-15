using Godot;
using Nerdbank.MessagePack;
using PolyType;
using System;
using System.Buffers.Binary;

/// <summary>
/// Position + Euler rotation snapshot sent by the authority for transform-synced objects.
/// <para>
/// This and the other per-tick sync states (<see cref="PlayerSync"/>, <see cref="MagnetSync"/>,
/// <see cref="WorldTickMessage"/>) are sent as arrays of values rather than maps of field names, which is about a
/// quarter smaller. The [Key] number is each field's place in the array: give a new field the next number, and never
/// renumber or reuse one, or older saves read into the wrong fields. A save from before a field was added reads it as
/// its default.
/// </para>
/// </summary>
[GenerateShape]
public partial record struct TransformSyncState
{
    [Key(0)] public Vector3 pos;
    [Key(1)] public Vector3 rot;
}

/// <summary>
/// What a Box3D body's authority sends: its transform plus its velocities, so followers can keep pace with it between
/// updates and a new authority can carry on at the same speed and spin.
/// <para>
/// There can be hundreds of these a tick, so they are packed by hand into 30 bytes (MessagePack takes 65): position as
/// full floats, rotation as 16-bit angles (steps of 0.0001 rad), and velocities as half floats (about 3 significant
/// digits). A body at rest leaves the velocities off (18 bytes), and they read back as zero.
/// </para>
/// </summary>
public struct BodySyncState
{
    public Vector3 pos;
    public Vector3 rot;
    public Vector3 vel;
    /// <summary>Angular velocity, world space (rad/s about each axis).</summary>
    public Vector3 angVel;

    private const int RestingSize = 18;
    private const int MovingSize = 30;

    /// <summary>A rotation angle (-π to π) times this is stored as a short.</summary>
    private const float AngleScale = short.MaxValue / Mathf.Pi;

    public byte[] Pack()
    {
        bool resting = vel == Vector3.Zero && angVel == Vector3.Zero;
        byte[] bytes;
        if (resting)
        {
            bytes = new byte[RestingSize];
        }
        else
        {
            bytes = new byte[MovingSize];
        }
        Span<byte> b = bytes;
        BinaryPrimitives.WriteSingleLittleEndian(b.Slice(0), pos.X);
        BinaryPrimitives.WriteSingleLittleEndian(b.Slice(4), pos.Y);
        BinaryPrimitives.WriteSingleLittleEndian(b.Slice(8), pos.Z);
        BinaryPrimitives.WriteInt16LittleEndian(b.Slice(12), PackAngle(rot.X));
        BinaryPrimitives.WriteInt16LittleEndian(b.Slice(14), PackAngle(rot.Y));
        BinaryPrimitives.WriteInt16LittleEndian(b.Slice(16), PackAngle(rot.Z));
        if (!resting)
        {
            BinaryPrimitives.WriteHalfLittleEndian(b.Slice(18), (Half)vel.X);
            BinaryPrimitives.WriteHalfLittleEndian(b.Slice(20), (Half)vel.Y);
            BinaryPrimitives.WriteHalfLittleEndian(b.Slice(22), (Half)vel.Z);
            BinaryPrimitives.WriteHalfLittleEndian(b.Slice(24), (Half)angVel.X);
            BinaryPrimitives.WriteHalfLittleEndian(b.Slice(26), (Half)angVel.Y);
            BinaryPrimitives.WriteHalfLittleEndian(b.Slice(28), (Half)angVel.Z);
        }
        return bytes;
    }

    public static BodySyncState Unpack(byte[] bytes)
    {
        ReadOnlySpan<byte> b = bytes;
        BodySyncState s = new BodySyncState();
        s.pos.X = BinaryPrimitives.ReadSingleLittleEndian(b.Slice(0));
        s.pos.Y = BinaryPrimitives.ReadSingleLittleEndian(b.Slice(4));
        s.pos.Z = BinaryPrimitives.ReadSingleLittleEndian(b.Slice(8));
        s.rot.X = BinaryPrimitives.ReadInt16LittleEndian(b.Slice(12)) / AngleScale;
        s.rot.Y = BinaryPrimitives.ReadInt16LittleEndian(b.Slice(14)) / AngleScale;
        s.rot.Z = BinaryPrimitives.ReadInt16LittleEndian(b.Slice(16)) / AngleScale;
        if (bytes.Length >= MovingSize)
        {
            s.vel.X = (float)BinaryPrimitives.ReadHalfLittleEndian(b.Slice(18));
            s.vel.Y = (float)BinaryPrimitives.ReadHalfLittleEndian(b.Slice(20));
            s.vel.Z = (float)BinaryPrimitives.ReadHalfLittleEndian(b.Slice(22));
            s.angVel.X = (float)BinaryPrimitives.ReadHalfLittleEndian(b.Slice(24));
            s.angVel.Y = (float)BinaryPrimitives.ReadHalfLittleEndian(b.Slice(26));
            s.angVel.Z = (float)BinaryPrimitives.ReadHalfLittleEndian(b.Slice(28));
        }
        return s;
    }

    private static short PackAngle(float angle)
    {
        return (short)Math.Clamp(Mathf.RoundToInt(angle * AngleScale), -short.MaxValue, short.MaxValue);
    }
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
