using System.Collections.Generic;

/// <summary>Named groups of the structure blueprints, for the "blueprints" console command.</summary>
public static class BlueprintSets
{
    public static readonly Dictionary<string, string[]> Sets = new()
    {
        ["conveyors"] = ["blueprint_conveyor_splitter", "blueprint_conveyor_splitter_switch", "blueprint_conveyor_loader"],
        ["advanced"] = ["blueprint_conveyor_adv", "blueprint_conveyor_adv_slope", "blueprint_conveyor_adv_loader"],
        ["magnetic"] = ["blueprint_conveyor_mag", "blueprint_conveyor_mag_wall", "blueprint_conveyor_mag_ceiling", "blueprint_conveyor_mag_turn_wall", "blueprint_conveyor_mag_turn_ceiling"],
        ["launchers"] = ["blueprint_launch_ramp", "blueprint_cannon", "blueprint_catapult"],
        ["sorting"] = ["blueprint_filter_basic", "blueprint_filter_arm"],
        ["fields"] = ["blueprint_antigrav_projector", "blueprint_zeropoint_projector"],
        ["processing"] = ["blueprint_plate_press", "blueprint_rod_extruder", "blueprint_polisher", "blueprint_smelter"],
        ["chutes"] = ["blueprint_chute_h_straight", "blueprint_chute_h_turn", "blueprint_chute_v_straight", "blueprint_chute_v_turn", "blueprint_chute_hopper_up", "blueprint_chute_dropper_down", "blueprint_chute_hopper_2x2", "blueprint_chute_hopper_3x3"],
        ["chutes_advanced"] = ["blueprint_chute_adv_h_straight", "blueprint_chute_adv_h_turn", "blueprint_chute_adv_v_straight", "blueprint_chute_adv_v_turn", "blueprint_chute_adv_hopper_up", "blueprint_chute_adv_dropper_down", "blueprint_chute_adv_hopper_2x2", "blueprint_chute_adv_hopper_3x3"],
    };
}
