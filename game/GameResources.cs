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
        new LevelInfo ("FluidSimTest","res://game/levels/FluidSimTest.tscn"),
        new LevelInfo ("FactoryGameTest","res://game/levels/FactoryGameTest.tscn"),
        new LevelInfo ("FactoryMap","res://game/levels/FactoryMap.tscn"),
    };

    public static Dictionary<string, (string inventoryItemPath, string worldItemPath, string handItemPath)> Items = new Dictionary<string, (string,string,string)>()
    {
        { "test_inert_cube",("res://game/items/inventoryItems/test_inert_cube.tres","res://game/items/examples/test_inert_cube_world.tscn","res://game/items/examples/test_inert_cube_hand.tscn") },

    };


}

