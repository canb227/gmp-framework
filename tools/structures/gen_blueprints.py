"""
Writes the blueprint definitions (.tres) for the new structures, the .import files for their icons, and
game/scripts/core/BlueprintSets.cs (the sets the "blueprints" console command hands out).

    python3 tools/structures/gen_blueprints.py

Icons are rendered separately: python3 tools/blender/run.py icons <family> (see each build script's ICONS).
"""
import os, sys, hashlib
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from scenegen import REPO, new_uid, res_to_abs

S = "res://game/scenes/structures/"
ICON = "res://game/assets/icons/blueprints/"

# (set, itemID, display name, description, scene, alternate scene or None)
BLUEPRINTS = [
    ("conveyors", "blueprint_conveyor_splitter", "Splitter Conveyor",
     "A wedge splits incoming items roughly evenly out of its left and right sides.", "conveyors/basic/ConveyorSplitter", None),
    ("conveyors", "blueprint_conveyor_splitter_switch", "Switchable Splitter",
     "A paddle sends every item out of the left or the right side. Flip the lever to switch.", "conveyors/basic/ConveyorSplitterSwitch", None),
    ("conveyors", "blueprint_conveyor_loader", "Loader Conveyor",
     "A conveyor whose end kicks up, lifting items over the wall of a conveyor running across its front.", "conveyors/basic/ConveyorLoader", None),

    ("advanced", "blueprint_conveyor_adv", "Advanced Conveyor",
     "A faster conveyor with high refurbished walls, for heavy throughput. Fed from one side, it turns to carry on forward.",
     "conveyors/advanced/ConveyorAdvanced", None),
    ("advanced", "blueprint_conveyor_adv_slope", "Advanced Slanted Conveyor",
     "Climbs one cell over two. Press T while placing to make it carry items downhill instead.",
     "conveyors/advanced/ConveyorAdvancedSlope", "conveyors/advanced/ConveyorAdvancedSlopeDown"),
    ("advanced", "blueprint_conveyor_adv_loader", "Advanced Loader Conveyor",
     "A high-walled conveyor whose end kicks items over the wall of a conveyor across its front.", "conveyors/advanced/ConveyorAdvancedLoader", None),

    ("magnetic", "blueprint_conveyor_mag", "Magnetic Conveyor",
     "A salvage conveyor fitted with electromagnets that hold items to the belt. Fed from one side, it turns to carry on forward.",
     "conveyors/magnetic/ConveyorMagnetic", None),
    ("magnetic", "blueprint_conveyor_mag_wall", "Magnetic Wall Conveyor",
     "A magnetic conveyor mounted on the side of its cell, carrying items along a wall.", "conveyors/magnetic/ConveyorMagneticWall", None),
    ("magnetic", "blueprint_conveyor_mag_ceiling", "Magnetic Ceiling Conveyor",
     "A magnetic conveyor mounted upside down on the top of its cell, carrying items along a ceiling.", "conveyors/magnetic/ConveyorMagneticCeiling", None),
    ("magnetic", "blueprint_conveyor_mag_turn_wall", "Magnetic Wall Conveyor Turn",
     "A magnetic turn mounted on the side of its cell. Press T while placing to mirror it.",
     "conveyors/magnetic/ConveyorMagneticWallTurnRight", "conveyors/magnetic/ConveyorMagneticWallTurnLeft"),
    ("magnetic", "blueprint_conveyor_mag_turn_ceiling", "Magnetic Ceiling Conveyor Turn",
     "A magnetic turn mounted upside down on the top of its cell. Press T while placing to mirror it.",
     "conveyors/magnetic/ConveyorMagneticCeilingTurnRight", "conveyors/magnetic/ConveyorMagneticCeilingTurnLeft"),

    ("launchers", "blueprint_launch_ramp", "Launch Ramp",
     "A fast belt that bends up and throws items off its lip. Three cells long, two tall.", "launchers/LaunchRamp", None),
    ("launchers", "blueprint_cannon", "Cannon",
     "Takes items in at the back and fires them from an elevated barrel. Two cells long, two tall.", "launchers/Cannon", None),
    ("launchers", "blueprint_catapult", "Catapult",
     "Items roll into the bucket at the back; the arm flings them forward. Three cells long, two tall.", "launchers/Catapult", None),

    ("sorting", "blueprint_filter_basic", "Basic Filter",
     "Drop sample items in the basket on top. Matching items on the belt are pushed out to the right, the rest go straight on.",
     "sorting/FilterBasic", None),
    ("sorting", "blueprint_filter_arm", "Filter Arm",
     "A salvaged grabber arm with a camera head that picks items off a neighbouring belt.", "sorting/FilterArm", None),

    ("fields", "blueprint_antigrav_projector", "Antigravity Projector",
     "Projects a field from its front face in which items lose their weight.", "fields/AntigravProjector", None),
    ("fields", "blueprint_zeropoint_projector", "Zero Point Projector",
     "Projects a long beam that removes gravity and slowly carries items along it.", "fields/ZeroPointProjector", None),

    ("processing", "blueprint_plate_press", "Plate Press",
     "Presses ingots on the belt into plates. One cell wide, two long.", "processing/PlatePress", None),
    ("processing", "blueprint_rod_extruder", "Rod Extruder",
     "Extrudes ingots on the belt into rods. One cell wide, two long.", "processing/RodExtruder", None),
    ("processing", "blueprint_polisher", "Polishing Machine",
     "Polishes whatever passes through it on the belt. Two cells wide, two long.", "processing/Polisher", None),
    ("processing", "blueprint_smelter", "Smelter",
     "Smelts ore dropped into its hopper; ingots come out at the front. Two cells in each direction.", "Smelter", None),
]

CHUTE_PIECES = [
    ("h_straight", "Chute", "A horizontal chute on free rollers; items coast along it.", "HStraight", None),
    ("h_turn", "Chute Turn", "Turns a horizontal chute 90 degrees. Press T while placing to mirror it.", "HTurnRight", "HTurnLeft"),
    ("v_straight", "Vertical Chute", "A vertical shaft items fall through.", "VStraight", None),
    ("v_turn", "Chute Elbow", "Catches items falling in from above and turns them out through the front at channel height.", "VTurn", None),
    ("hopper_up", "Chute Hopper", "A funnel that catches falling items and feeds the chute below it.", "HopperUp", None),
    ("dropper_down", "Chute Dropper", "Holds items fed in from above behind a trapdoor. Flip its lever to drop them.", "DropperDown", None),
    ("hopper_2x2", "Large Chute Hopper", "A 2x2x2 funnel that catches falling items and feeds a chute under its anchor cell.", "Hopper2x2", None),
    ("hopper_3x3", "Huge Chute Hopper", "A 3x3x2 funnel that catches falling items and feeds a chute under its centre.", "Hopper3x3", None),
]
for _tier, _set, _pre, _scene, _label, _extra in (
        ("basic", "chutes", "chute_", "Chute", "", ""),
        ("adv", "chutes_advanced", "chute_adv_", "ChuteAdv", "Powered ", " When powered it pushes items along gently.")):
    for key, name, desc, scene, alt in CHUTE_PIECES:
        BLUEPRINTS.append((_set, f"blueprint_{_pre}{key}", _label + name, desc + _extra,
                           f"chutes/{_scene}{scene}", f"chutes/{_scene}{alt}" if alt else None))

ICON_IMPORT = """[remap]

importer="texture"
type="CompressedTexture2D"
uid="{uid}"
path="res://.godot/imported/{name}-{md5}.ctex"
metadata={{
"vram_texture": false
}}

[deps]

source_file="{res}"
dest_files=["res://.godot/imported/{name}-{md5}.ctex"]

[params]

compress/mode=0
compress/high_quality=false
compress/lossy_quality=0.7
compress/uastc_level=0
compress/rdo_quality_loss=0.0
compress/hdr_compression=1
compress/normal_map=0
compress/channel_pack=0
mipmaps/generate=false
mipmaps/limit=-1
roughness/mode=0
roughness/src_normal=""
process/channel_remap/red=0
process/channel_remap/green=1
process/channel_remap/blue=2
process/channel_remap/alpha=3
process/fix_alpha_border=true
process/premult_alpha=false
process/normal_map_invert_y=false
process/hdr_as_srgb=false
process/hdr_clamp_exposure=false
process/size_limit=0
detect_3d/compress_to=1
"""

def icon_uid(res):
    path = res_to_abs(res)
    if not os.path.exists(path):
        raise FileNotFoundError(f"missing icon {res}: render it with tools/blender/run.py icons <family>")
    imp = path + ".import"
    if os.path.exists(imp):
        return open(imp).read().split('uid="', 1)[1].split('"', 1)[0]
    uid = new_uid(res)
    open(imp, "w", newline="\n").write(ICON_IMPORT.format(uid=uid, name=os.path.basename(res),
                                                           md5=hashlib.md5(res.encode()).hexdigest(), res=res))
    return uid

TRES = """[gd_resource type="Resource" script_class="BlueprintItem" format=3]

[ext_resource type="Script" uid="uid://blmd28f80x7rh" path="res://game/scripts/items/BlueprintItem.cs" id="1_bp"]
[ext_resource type="Texture2D" uid="{icon_uid}" path="{icon}" id="2_icon"]
[ext_resource type="PackedScene" path="res://game/scenes/structures/BlueprintGhost.tscn" id="3_ghost"]
[ext_resource type="PackedScene" path="{scene}" id="4_struct"]
{alt_ext}
[resource]
script = ExtResource("1_bp")
structureScene = ExtResource("4_struct")
{alt_prop}itemID = "{item}"
displayName = "Blueprint: {name}"
description = "{desc}"
icon = ExtResource("2_icon")
inHandScene = ExtResource("3_ghost")
maxStackSize = 10
"""

def main():
    sets = {}
    for set_, item, name, desc, scene, alt in BLUEPRINTS:
        icon = ICON + item + ".png"
        scene_res, alt_res = S + scene + ".tscn", (S + alt + ".tscn" if alt else None)
        for r in (scene_res, alt_res):
            if r and not os.path.exists(res_to_abs(r)):
                raise FileNotFoundError(r)
        text = TRES.format(icon_uid=icon_uid(icon), icon=icon, scene=scene_res, item=item, name=name, desc=desc,
                           alt_ext=f'[ext_resource type="PackedScene" path="{alt_res}" id="5_alt"]\n' if alt else "",
                           alt_prop='alternateStructureScene = ExtResource("5_alt")\n' if alt else "")
        folder = os.path.join(REPO, "game", "definitions", "blueprints", set_)
        os.makedirs(folder, exist_ok=True)
        open(os.path.join(folder, item + ".tres"), "w", newline="\n").write(text)
        sets.setdefault(set_, []).append(item)
    lines = ["// Generated by tools/structures/gen_blueprints.py; edit the table there, not this file.",
             "using System.Collections.Generic;", "",
             "/// <summary>Named groups of the structure blueprints, for the \"blueprints\" console command.</summary>",
             "public static class BlueprintSets", "{",
             "    public static readonly Dictionary<string, string[]> Sets = new()", "    {"]
    for k, v in sets.items():
        lines.append(f'        ["{k}"] = [' + ", ".join(f'"{x}"' for x in v) + "],")
    lines += ["    };", "}", ""]
    open(os.path.join(REPO, "game", "scripts", "core", "BlueprintSets.cs"), "w", newline="\n").write("\n".join(lines))
    print(f"{len(BLUEPRINTS)} blueprints in {len(sets)} sets: " + ", ".join(f"{k} ({len(v)})" for k, v in sets.items()))

if __name__ == "__main__":
    main()
