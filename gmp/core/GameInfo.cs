using PolyType;
using System;
using System.Collections.Generic;

[GenerateShape]
public partial record LevelInfo : IEquatable<LevelInfo>
{
    public string levelName;
    public string levelPath;
    public string iconPath;

    public LevelInfo(string levelName, string levelPath, string iconPath = "")
    {
        this.levelName = levelName;
        this.levelPath = levelPath;
        this.iconPath = iconPath;
    }
}



/// <summary>What each player starts with (chosen in the debug lobby).</summary>
public enum StarterItems { None, SingleMagnetRod, All }

//Literally every single piece of data that you need to start a game MUST go in here.
[GenerateShape]
public partial record GameInfo
{
    public int levelIdx;
    public StarterItems starterItems = StarterItems.All;
    /// <summary>Players can grab items with an empty hand (A/B test against the single-item magnet rod).</summary>
    public bool emptyHandGrab = true;
    /// <summary>A save file name in <c>GameWorld.SaveDir</c> to start from instead of the level; empty starts fresh.</summary>
    public string saveName;
    public Dictionary<ulong, PlayerInfo> Players;
    public ulong tick;
}
