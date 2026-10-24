using PolyType;

/// <summary>A level the lobby can start (listed in <c>GameResources.LevelsList</c>).</summary>
public record LevelInfo
{
    /// <summary>Shown in the lobby's level list.</summary>
    public string levelName;
    /// <summary>The level scene the host spawns.</summary>
    public string levelPath;

    public LevelInfo(string levelName, string levelPath)
    {
        this.levelName = levelName;
        this.levelPath = levelPath;
    }
}

/// <summary>What each player starts with besides the gravity gun, which everyone has (chosen in the debug lobby).</summary>
public enum StarterItems { None, Tools, All }

/// <summary>
/// The settings a game starts from, chosen by the host in the lobby and sent to every peer (<see cref="Lobby.gameInfo"/>).
/// Anything every peer must know before the game starts goes here. Saves keep it too.
/// </summary>
[GenerateShape]
public partial record GameInfo
{
    /// <summary>Index in <c>GameResources.LevelsList</c> of the level to play.</summary>
    public int levelIdx;
    public StarterItems starterItems = StarterItems.All;
    /// <summary>A save file to start from instead of the level (chosen with the lobby's Load Game); empty starts fresh.</summary>
    public string savePath;
}
