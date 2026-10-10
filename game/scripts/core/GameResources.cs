using System.Collections.Generic;


public static class GameResources
{

    public static List<LevelInfo> LevelsList = new List<LevelInfo>
    {
        new LevelInfo ("FactoryMap","res://game/scenes/levels/FactoryMap.tscn"),
        new LevelInfo ("TestFacility","res://game/scenes/levels/test_facility.scn"),
        new LevelInfo ("DevArea","res://game/scenes/levels/intro_blockout/dev_area.tscn"),
        new LevelInfo ("PhysicsTestbed","res://dev/physics_testbed/PhysicsTestbed.tscn"),
    };

}

