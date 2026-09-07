using PolyType;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

[GenerateShape]
public partial record LevelInfo : IEquatable<LevelInfo>
{
    public string levelName;
    public string levelPath;

    public LevelInfo(string levelName, string levelPath)
    {
        this.levelName = levelName;
        this.levelPath = levelPath;
    }
}



//Literally every single piece of data that you need to start a game MUST go in here.
[GenerateShape]
public partial record GameInfo
{
    public LevelInfo Level;
    public Dictionary<ulong, PlayerInfo> Players;
    public ulong tick;
    public int bulk1size;
    public int bulk2size;
    public int bulk1flag;
    public int bulk2flag;
    public byte[] bulk1;
    public byte[] bulk2;
}
