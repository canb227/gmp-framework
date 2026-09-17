using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


public static class GameResources
{

    public static List<LevelInfo> LevelsList = new List<LevelInfo>
    {
        new LevelInfo ("ObjectMuseum","res://game/levels/ObjectMuseum.tscn"),
        new LevelInfo ("FactoryMap","res://game/levels/FactoryMap.tscn"),
    };

    public static Dictionary<string, string> FactoryItems = new Dictionary<string, string>()
    {
        { "test_cube","res://game/items/examples/TestFactoryCube.tscn" },
        { "test_sphere","res://game/items/examples/TestFactorySphere.tscn" },
        { "te3e","res://game/items/examples/TestFactoryCube.tscn" },
        { "test4_cube","res://game/items/examples/TestFactoryCube.tscn" },
        { "test3_cube","res://game/items/examples/TestFactoryCube.tscn" },

    };
}

