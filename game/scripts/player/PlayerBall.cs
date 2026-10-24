using Godot;
using Nerdbank.MessagePack;
using PolyType;

/// <summary>
/// A downed player: just their head, a dynamic ball the physics world simulates on the player's peer and syncs like
/// any dynamic body. Spawned and despawned by the player (<see cref="FactoryPlayer.GoDown"/>, <see cref="FactoryPlayer.Revive"/>).
/// On every peer it hides its player while it exists; the player's peer rides the player along inside it.
/// </summary>
public partial class PlayerBall : GMPOBody
{
    /// <summary>The downed player's object id, and how fast the ball sets off (the player's motion plus the hit's push).</summary>
    PlayerBallState state;
    byte[] stateBytes;

    public override void _Ready()
    {
        base._Ready();
        // The player comes back from it; a save has no downed players.
        AddToGroup(GameWorld.UnsavedGroup);
    }

    public override void AfterInit()
    {
        base.AfterInit();
        FactoryPlayer player = Player();
        if (player != null)
        {
            player.Visible = false;
        }
        if (authority == Lobby.selfPeerID)
        {
            Call("set_linear_velocity", state.velocity);
            // A little tumble, so it rolls off rather than sliding.
            Call("set_angular_velocity", new Vector3(state.velocity.Z, 0, -state.velocity.X) * 2f);
        }
    }

    public override void OnDespawned(DespawnReason reason)
    {
        FactoryPlayer player = Player();
        if (player != null)
        {
            player.Visible = true;
        }
    }

    /// <summary>The player this ball is, or null (gone).</summary>
    FactoryPlayer Player()
    {
        if (GameWorld.syncedObjs.TryGetValue(state.playerID, out GMPObject obj))
        {
            return obj as FactoryPlayer;
        }
        return null;
    }

    /// <summary>The spawn state, so late joiners also hide the player. Its pose is synced by the physics world.</summary>
    public override byte[] GenerateStateUpdate()
    {
        return stateBytes;
    }

    public override void ApplyStateUpdate(byte[] update)
    {
        if (update == null || update.Length == 0) return;
        stateBytes = update;
        state = GMPObject.serializer.Deserialize<PlayerBallState>(update);
    }
}

[GenerateShape]
public partial record struct PlayerBallState
{
    [Key(0)] public ulong playerID;
    [Key(1)] public Vector3 velocity;
}
