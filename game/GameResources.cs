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

    public static Dictionary<string, (string inventoryItemPath, string worldItemPath, string handItemPath)> Items = new Dictionary<string, (string,string,string)>()
    {
        { "test_inert_cube",("res://game/items/inventoryItems/test_inert_cube.tres","res://game/items/examples/test_inert_cube_world.tscn","res://game/items/examples/test_inert_cube_hand.tscn") },
        { "test_placeable_1x1x1",("res://game/items/inventoryItems/test_placeable_1x1x1.tres","res://game/items/factoryMachines/test_placeable_1x1x1_world.tscn","res://game/items/factoryMachines/test_placeable_1x1x1_hand.tscn") }
    };


}

