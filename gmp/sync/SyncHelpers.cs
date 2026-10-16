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
/// There can be hundreds of these a tick, so they are packed by hand into 31 bytes (MessagePack takes 65): position as
/// full floats, rotation as a quaternion's three smallest parts in 16 bits each plus which part was left out (about
/// 0.003° steps), and velocities as half floats (about 3 significant digits). A body at rest leaves the velocities off
/// (19 bytes), and they read back as zero.
/// </para>
/// <para>
/// Rotation used to be sent as Euler angles (18 and 30 bytes), which saves from before still hold. Turning angles into a
/// quaternion takes sin and cos, which measured about 1 µs per state on the client; the quaternion needs one square root.
/// </para>
/// </summary>
public struct BodySyncState
{
    public Vector3 pos;
    public Quaternion rot;
    public Vector3 vel;
    /// <summary>Angular velocity, world space (rad/s about each axis).</summary>
    public Vector3 angVel;

    private const int RestingSize = 19;
    private const int MovingSize = 31;
    /// <summary>The sizes of the old Euler-angle format, still read from saves.</summary>
    private const int OldRestingSize = 18;
    private const int OldMovingSize = 30;

    /// <summary>A rotation angle (-π to π) times this was stored as a short, in the old format.</summary>
    private const float AngleScale = short.MaxValue / Mathf.Pi;

    /// <summary>
    /// A quaternion part other than the largest is at most 1/√2 in size, so times this it fits a short.
    /// </summary>
    private const float PartScale = short.MaxValue * Mathf.Sqrt2;

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

        // The largest part is left out (the other three give it back, as the quaternion has length 1). With it made
        // positive, which is the same rotation, it needs no sign either.
        Quaternion q = rot.Normalized();
        int largest = 0;
        float largestSize = Math.Abs(q.X);
        if (Math.Abs(q.Y) > largestSize)
        {
            largest = 1;
            largestSize = Math.Abs(q.Y);
        }
        if (Math.Abs(q.Z) > largestSize)
        {
            largest = 2;
            largestSize = Math.Abs(q.Z);
        }
        if (Math.Abs(q.W) > largestSize)
        {
            largest = 3;
        }
        if (q[largest] < 0)
        {
            q = -q;
        }
        int written = 0;
        for (int i = 0; i < 4; i++)
        {
            if (i != largest)
            {
                BinaryPrimitives.WriteInt16LittleEndian(b.Slice(12 + 2 * written), PackPart(q[i]));
                written++;
            }
        }
        b[18] = (byte)largest;

        if (!resting)
        {
            BinaryPrimitives.WriteHalfLittleEndian(b.Slice(19), (Half)vel.X);
            BinaryPrimitives.WriteHalfLittleEndian(b.Slice(21), (Half)vel.Y);
            BinaryPrimitives.WriteHalfLittleEndian(b.Slice(23), (Half)vel.Z);
            BinaryPrimitives.WriteHalfLittleEndian(b.Slice(25), (Half)angVel.X);
            BinaryPrimitives.WriteHalfLittleEndian(b.Slice(27), (Half)angVel.Y);
            BinaryPrimitives.WriteHalfLittleEndian(b.Slice(29), (Half)angVel.Z);
        }
        return bytes;
    }

    public static BodySyncState Unpack(byte[] bytes)
    {
        if (bytes.Length == OldRestingSize || bytes.Length == OldMovingSize)
        {
            return UnpackOld(bytes);
        }
        ReadOnlySpan<byte> b = bytes;
        BodySyncState s = new BodySyncState();
        s.pos.X = BinaryPrimitives.ReadSingleLittleEndian(b.Slice(0));
        s.pos.Y = BinaryPrimitives.ReadSingleLittleEndian(b.Slice(4));
        s.pos.Z = BinaryPrimitives.ReadSingleLittleEndian(b.Slice(8));

        int largest = b[18];
        float sumOfSquares = 0;
        int read = 0;
        Quaternion q = new Quaternion();
        for (int i = 0; i < 4; i++)
        {
            if (i != largest)
            {
                float part = BinaryPrimitives.ReadInt16LittleEndian(b.Slice(12 + 2 * read)) / PartScale;
                q[i] = part;
                sumOfSquares += part * part;
                read++;
            }
        }
        q[largest] = Mathf.Sqrt(Math.Max(0, 1 - sumOfSquares));
        s.rot = q;

        if (bytes.Length >= MovingSize)
        {
            s.vel.X = (float)BinaryPrimitives.ReadHalfLittleEndian(b.Slice(19));
            s.vel.Y = (float)BinaryPrimitives.ReadHalfLittleEndian(b.Slice(21));
            s.vel.Z = (float)BinaryPrimitives.ReadHalfLittleEndian(b.Slice(23));
            s.angVel.X = (float)BinaryPrimitives.ReadHalfLittleEndian(b.Slice(25));
            s.angVel.Y = (float)BinaryPrimitives.ReadHalfLittleEndian(b.Slice(27));
            s.angVel.Z = (float)BinaryPrimitives.ReadHalfLittleEndian(b.Slice(29));
        }
        return s;
    }

    /// <summary>The old format, with rotation as Euler angles (saves from before 2026-10-16).</summary>
    private static BodySyncState UnpackOld(byte[] bytes)
    {
        ReadOnlySpan<byte> b = bytes;
        BodySyncState s = new BodySyncState();
        s.pos.X = BinaryPrimitives.ReadSingleLittleEndian(b.Slice(0));
        s.pos.Y = BinaryPrimitives.ReadSingleLittleEndian(b.Slice(4));
        s.pos.Z = BinaryPrimitives.ReadSingleLittleEndian(b.Slice(8));
        Vector3 euler;
        euler.X = BinaryPrimitives.ReadInt16LittleEndian(b.Slice(12)) / AngleScale;
        euler.Y = BinaryPrimitives.ReadInt16LittleEndian(b.Slice(14)) / AngleScale;
        euler.Z = BinaryPrimitives.ReadInt16LittleEndian(b.Slice(16)) / AngleScale;
        s.rot = Quaternion.FromEuler(euler);
        if (bytes.Length == OldMovingSize)
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

    private static short PackPart(float part)
    {
        return (short)Math.Clamp(Mathf.RoundToInt(part * PartScale), -short.MaxValue, short.MaxValue);
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
