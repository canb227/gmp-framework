using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


public static class GameResources
{

    public static List<LevelInfo> LevelsList = new List<LevelInfo>
    {
        new LevelInfo ("B3DTestScene", "res://gmp/examples/SyncedBox3DLevel.tscn"),
        new LevelInfo ("B3DTerrainTest", "res://game/levels/B3DTerrainTest.tscn"),
    };
}

