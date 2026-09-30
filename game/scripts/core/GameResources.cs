using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


public static class GameResources
{

    public static List<LevelInfo> LevelsList = new List<LevelInfo>
    {
        new LevelInfo ("ObjectMuseum","res://game/scenes/levels/ObjectMuseum.tscn"),
        new LevelInfo ("FactoryMap","res://game/scenes/levels/FactoryMap.tscn"),
        new LevelInfo ("TestFacility","res://game/scenes/levels/test_facility.scn"),
        new LevelInfo ("DevArea","res://game/scenes/levels/dev_area.tscn"),
    };

}

