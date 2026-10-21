using PolyType;

/// <summary>A lobby member as every peer knows it (<see cref="Lobby.members"/>); the roster sends these.</summary>
[GenerateShape]
public partial class PlayerInfo
{
    public ulong PeerID;
    /// <summary>The name the player chose (their Steam name, or --name on LAN).</summary>
    public string Name;
    public bool IsHost;
    /// <summary>This peer has said DonePreloading / DoneLoading for the game being started (see <see cref="Lobby"/>).</summary>
    public bool donePreloading;
    public bool doneLoading;
}

// Source-generated serialization needs a shape for each array type it serializes; PolyType collects them here.
[GenerateShapeFor<PlayerInfo[]>]
partial class Witness;
