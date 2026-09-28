"""
Writes the world scenes (.tscn), item definitions (.tres) and import files for the resource and product items
whose models come from game/assets/models/items/source/build_items.py. The ITEMS table below is the single
source of truth for each item's physics, tags and museum text; place_museum.py reads it for the Materials Wing.

    python3 tools/items/gen_items.py

Tags are ItemTags values (game/scripts/items/ItemTags.cs). The interactions they are meant to have are listed in
TagInteractions.cs; most of them only log for now.
"""
import os, sys, hashlib
sys.path.insert(0, os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "structures"))
from scenegen import REPO, new_uid, res_to_abs, ensure_import, script_uid, f
from gen_blueprints import icon_uid

# ItemTags (append-only in ItemTags.cs)
INERT, HOT, COLD, METAL, ROCK, RUBBER, CONCRETE, PLASTIC, GLASS = range(9)
FUEL, FRAGILE, STICKY, MAGNETIC, VOLATILE, SOLUBLE, WET, SLIPPERY, BUOYANT, LIQUID, CHARGED, CONDUCTIVE, INSULATED, FERROUS = range(9, 23)

MODELS = "res://game/assets/models/items/"
IMPORT = "res://game/assets/models/shared/salvage_import.gd"
ITEM_SCRIPT = "res://game/scripts/items/world/PhysicalFactoryItem.cs"
INFO_SCRIPT = "res://game/scripts/items/ItemInfo.cs"

def box(x, y, z): return ("box", (x, y, z))
def sphere(r): return ("sphere", r)
def capsule(r, h): return ("capsule", (r, h))

# id, name, group, shape, physics {density, friction, rolling, restitution, damping, gravity}, tags,
# difficulty (1-5), made from, behaviour text, challenge text
ITEMS = [
    # ------------------------------------------------------------------ metals
    ("iron_ingot", "Iron Ingot", "metals", box(0.8, 0.3, 0.4), dict(density=7.0, friction=0.5), [METAL, FERROUS], 1,
     "Iron Ore -> Smelter",
     "Dense, flat-sided bar. Sits still on a belt, stacks neatly and never rolls. Ferrous: magnets and lodestone grab it.",
     "The baseline item. Its weight makes launch ramps fall short and it needs powered chutes on long runs."),
    ("iron_rod", "Iron Rod", "metals", capsule(0.1, 1.2), dict(density=7.0, friction=0.4, rolling=0.02), [METAL, FERROUS], 2,
     "Iron Ingot -> Rod Extruder",
     "Long, round bar. Rolls sideways at the slightest bump and swings round on turns.",
     "Lengthwise it slides into chutes. Crosswise it bridges them and jams. Guard rails and aligning funnels matter."),
    ("iron_plate", "Iron Plate", "metals", box(0.8, 0.08, 0.8), dict(density=7.0, friction=0.35), [METAL, FERROUS], 2,
     "Iron Ingot -> Plate Press",
     "Thin checker plate with a low centre of gravity. Slides a long way when shoved and stacks into tidy piles.",
     "Plates skate off the ends of fast belts and lie flat over hopper mouths, bridging them shut until knocked in."),
    ("copper_ingot", "Copper Ingot", "metals", box(0.8, 0.3, 0.4), dict(density=8.0, friction=0.4), [METAL, CONDUCTIVE], 1,
     "Copper Ore -> Smelter",
     "Heavier than iron and slicker. Conductive: it grounds voltaic charge. Not ferrous, so magnets ignore it.",
     "Easy to move. The trick is separating it from iron: a magnetic belt lifts the iron and lets the copper pass."),
    ("copper_rod", "Copper Rod", "metals", capsule(0.1, 1.2), dict(density=8.0, friction=0.35, rolling=0.02), [METAL, CONDUCTIVE], 2,
     "Copper Ingot -> Rod Extruder",
     "Rolls like the iron rod. Winds into magnet cores and makes a lightning rod for voltaic crystals.",
     "Same rolling problems as iron rods, but magnetic belts can't hold it, so it needs walls or tubes instead."),
    ("copper_plate", "Copper Plate", "metals", box(0.8, 0.08, 0.8), dict(density=8.0, friction=0.3), [METAL, CONDUCTIVE], 2,
     "Copper Ingot -> Plate Press",
     "Slick conductive plate. Touching a voltaic crystal drains its charge safely. Half of every battery cell.",
     "A copper plate lining a chute is a cheap 'grounding strip' that defuses charged crystals as they pass."),
    ("scrap_ball", "Scrap Ball", "metals", sphere(0.36), dict(density=3.0, friction=0.9, rolling=0.5, restitution=0.15), [METAL, FERROUS], 2,
     "salvage / Grinder waste",
     "A crushed bale of mixed metal. Lumpy, so it rolls in lurches and bounces off in odd directions. Weakly ferrous.",
     "Unpredictable. It tumbles out of turns and ricochets in hoppers, so give it wide, walled routes."),
    ("scrap_ingot", "Scrap Ingot", "metals", box(0.8, 0.3, 0.4), dict(density=6.0, friction=0.55), [METAL, FERROUS], 3,
     "Scrap Ball -> Smelter",
     "A cheap mixed-metal ingot. Every one comes out a different weight, depending on what went into the bale.",
     "Recipes want a quality ingot: weigh them on a tipping bucket or counterweight and send the light ones back round."),
    # ------------------------------------------------------------------ base resources
    ("coal", "Coal", "base", sphere(0.34), dict(density=1.3, friction=0.9, rolling=1.2), [ROCK, FUEL], 1,
     "mined",
     "Light, grippy lumps that never roll. Fuel: touching anything hot makes it burn and turn hot itself.",
     "Easy to carry, but a hot ingot dropped on a coal belt starts a fire that spreads lump to lump down the line."),
    ("salt_crystal", "Salt Crystal", "base", box(0.5, 0.5, 0.5), dict(density=2.2, friction=0.7), [ROCK, SOLUBLE], 1,
     "mined",
     "A perfect cube: stacks, packs and counts neatly. Soluble: it dissolves when wet and melts frost it touches.",
     "Keep it away from quench tanks. Used on purpose, a salt cube is the de-icer that clears a frost jam."),
    ("floatstone", "Floatstone", "base", sphere(0.4), dict(density=0.15, friction=0.6, rolling=0.3, damping=0.8, gravity=0.6), [ROCK, BUOYANT], 2,
     "mined",
     "Pumice so light it drifts. Fans, launch ramps and field projectors blow it off course. Floats in water and magma.",
     "Needs covered belts and roofed chutes. Being buoyant, it rides the Magma Bath's weir and splits itself from heavy ore."),
    ("frost_crystal", "Frost Crystal", "base", box(0.5, 0.5, 0.5), dict(density=0.9, friction=0.02, restitution=0.05), [COLD, GLASS, SLIPPERY], 3,
     "mined",
     "Nearly frictionless, and always cold. Belts slide out from under it instead of carrying it. It chills whatever it touches.",
     "Belts can't move it: push it with tube walls, paddles or gravity. Heat turns it to water, salt melts it, and it's the best coolant."),
    ("lodestone", "Lodestone", "base", sphere(0.4), dict(density=6.0, friction=0.7, rolling=0.4), [ROCK, MAGNETIC], 3,
     "mined",
     "Naturally magnetic. It tugs ferrous items toward itself, clumps with them and clings to magnetic belts.",
     "Lodestone gathers iron into jams in hoppers and splitters, so keep it separate. Heating it past the Curie point switches its field off for a while."),
    ("quartz_crystal", "Quartz Crystal", "base", capsule(0.18, 0.9), dict(density=2.6, friction=0.3, restitution=0.3), [GLASS, FRAGILE], 3,
     "mined",
     "A long, glassy point that rolls. Fragile: any hard knock shatters it into quartz shards, including a drop of more than one cell, a launcher or a collision.",
     "Gentle handling only: slow belts, rubber-padded chutes and elevators in place of drops. Every crash wastes material."),
    ("latex_resin", "Latex Resin", "base", sphere(0.33), dict(density=1.0, friction=2.0, rolling=2.0, restitution=0.0, damping=0.5), [RUBBER, STICKY], 4,
     "tapped",
     "A tacky, dead-soft blob. It glues itself to belts, walls and other items, and blobs merge into growing clumps.",
     "Clumps block hoppers and splitters. Chill it with cold items or the cryo vent so it hardens enough to move, then heat it to cure it into rubber."),
    ("sulfur", "Sulfur", "base", sphere(0.35), dict(density=2.0, friction=0.6, rolling=0.6), [ROCK, VOLATILE], 4,
     "mined",
     "Brittle yellow crystal. Volatile: a hot item or a voltaic spark sets it off, blasting its neighbours away and destroying it.",
     "One stray hot item can chain-react a whole belt. Route it clear of heaters and charged crystals, and space it out."),
    ("quicksilver", "Quicksilver", "base", sphere(0.28), dict(density=13.0, friction=0.05, rolling=0.0, restitution=0.0), [METAL, LIQUID], 5,
     "tapped",
     "A bead of liquid metal: very heavy, frictionless and it never stops rolling. Two beads that touch merge into one bigger bead, and a hard knock splits it again.",
     "It escapes through every gap and runs down belt slopes. It has to be enclosed end to end, metered by weight and kept apart so beads don't merge."),
    ("voltaic_crystal", "Voltaic Crystal", "base", box(0.5, 0.7, 0.5), dict(density=2.4, friction=0.5), [GLASS, CHARGED], 5,
     "mined",
     "A crystal that holds charge. Two crystals repel, so they won't sit together. It arcs into conductive metal and sparks off volatile items.",
     "It spreads itself out on belts and won't pile into a hopper. Ground it on copper before bulk handling, and never let it near sulfur."),
    # ------------------------------------------------------------------ products
    ("coke_briquette", "Coke Briquette", "products", box(0.45, 0.3, 0.45), dict(density=1.0, friction=0.8), [ROCK, FUEL], 1,
     "Coal -> Tunnel Furnace",
     "Baked coal pillows that stack. Burns much hotter than coal but only in a smelter, so it doesn't spread fire.",
     "The safe fuel: once coal is coked, fire risk stops being a routing concern."),
    ("glass_pane", "Glass Pane", "products", box(0.9, 0.05, 0.9), dict(density=2.5, friction=0.15), [GLASS, FRAGILE, SLIPPERY], 3,
     "Quartz Crystal -> Smelter",
     "A thin, slippery, fragile sheet. It sleds the length of a belt at every stop and cracks if it lands on an edge.",
     "Gentle stops, flat drops and stacking rather than piling."),
    ("quartz_shards", "Quartz Shards", "products", sphere(0.22), dict(density=2.6, friction=0.7, rolling=0.8), [GLASS], 2,
     "Quartz Crystal, shattered",
     "What's left of a dropped crystal. Small and sharp, so it catches in grates and gaps.",
     "Still smelts into glass, at half the yield. A shard count is a score for how rough the handling was."),
    ("rubber_ball", "Rubber Ball", "products", sphere(0.3), dict(density=1.1, friction=0.9, restitution=0.95), [RUBBER], 3,
     "Latex Resin -> heat (cure)",
     "Cured latex that bounces back almost as high as it fell, off every surface.",
     "Hard to contain: it leaps out of open hoppers. Turned round, rubber lining gives quartz and glass a soft landing."),
    ("blast_charge", "Blast Charge", "products", capsule(0.2, 0.7), dict(density=1.5, friction=0.6), [METAL, VOLATILE], 4,
     "Sulfur + Coal -> Assembly Chamber",
     "Sulfur and coal packed in a steel can. Heat, a spark or a hard knock sets it off.",
     "Planned as launcher propellant: one charge flings a load. Moving live charges demands everything learned from sulfur."),
    ("battery_cell", "Battery Cell", "products", capsule(0.22, 0.6), dict(density=3.0, friction=0.6), [METAL, CHARGED], 3,
     "Voltaic Crystal + Copper Plate -> Assembly Chamber",
     "Voltaic charge sealed in a copper cell. It stacks safely, but touching bare copper still drains it slowly.",
     "Planned as portable power for machines. Keep cells off copper belts and grounding strips."),
    ("magnet_core", "Magnet Core", "products", box(0.5, 0.5, 0.5), dict(density=6.0, friction=0.6), [METAL, MAGNETIC], 2,
     "Lodestone + Copper Rod -> Plate Press",
     "Pressed lodestone wound with copper: a strong, fixed magnet with marked poles.",
     "Crafting part for magnetic belts and tools. It pulls iron to it just as lodestone does, so handle it the same way."),
    ("aerogel_tile", "Aerogel Tile", "products", box(0.8, 0.1, 0.8), dict(density=0.05, friction=0.5, damping=0.9, gravity=0.5), [PLASTIC, BUOYANT, INSULATED], 2,
     "Floatstone -> Plate Press",
     "Frozen smoke: the lightest solid in the game. Insulating: nothing hot, cold or charged passes through it.",
     "It flutters off belts like floatstone. Laid under or between items, it's the shield that lets volatile and charged loads travel."),
]

GROUP_DIRS = {"metals": "metals", "base": "base_materials", "products": "products"}
SCENE_DIRS = {"metals": "metals", "base": "resources", "products": "products"}
MODEL_OF = {}                                              # item id -> glb basename (same as the id)

SCENE = """[gd_scene format=3 uid="{uid}"]

[ext_resource type="Script" uid="{script_uid}" path="{script}" id="1_item"]
[ext_resource type="PackedScene" uid="{model_uid}" path="{model}" id="2_model"]

[node name="{id}" type="Box3DBody"]
{shape}
{physics}
script = ExtResource("1_item")
itemID = "{id}"
canBePickTarget = true
canBeGrabbed = true
tags = Array[int]([{tags}])

[node name="Model" parent="." instance=ExtResource("2_model")]
"""

TRES = """[gd_resource type="Resource" script_class="ItemInfo" format=3]

[ext_resource type="Script" uid="{info_uid}" path="{info}" id="1_fi"]
[ext_resource type="Texture2D" uid="{icon_uid}" path="{icon}" id="2_icon"]
[ext_resource type="PackedScene" uid="{scene_uid}" path="{scene}" id="3_scene"]

[resource]
script = ExtResource("1_fi")
itemID = "{id}"
droppedScene = ExtResource("3_scene")
displayName = "{name}"
description = "{desc}"
icon = ExtResource("2_icon")
maxStackSize = 1
"""

def scene_res(item_id, group):
    return f"res://game/scenes/items/world/{SCENE_DIRS[group]}/{item_id}.tscn"

def shape_text(shape):
    kind, v = shape
    if kind == "box":
        return f"shape_type = 0\nbox_size = Vector3({f(v[0])}, {f(v[1])}, {f(v[2])})"
    if kind == "sphere":
        return f"shape_type = 1\nsphere_radius = {f(v)}"
    return f"shape_type = 2\ncapsule_radius = {f(v[0])}\ncapsule_height = {f(v[1])}"

def physics_text(p):
    names = {"density": "density", "friction": "friction", "rolling": "rolling_resistance", "restitution": "restitution",
             "damping": "linear_damping", "gravity": "gravity_scale"}
    return "\n".join(f"{names[k]} = {f(v)}" for k, v in p.items())

def existing_uid(path):
    if os.path.exists(path):
        head = open(path, encoding="utf-8").readline()
        if 'uid="' in head:
            return head.split('uid="', 1)[1].split('"', 1)[0]
    return None

def main():
    item_uid, info_uid = script_uid(ITEM_SCRIPT), script_uid(INFO_SCRIPT)
    for (iid, name, group, shape, phys, tags, diff, source, behaviour, challenge) in ITEMS:
        model = f"{MODELS}{iid}.glb"
        if not os.path.exists(res_to_abs(model)):
            raise FileNotFoundError(f"{model}: build it with tools/blender/run.py build items")
        model_uid = ensure_import(model, IMPORT)
        sres = scene_res(iid, group)
        spath = res_to_abs(sres)
        os.makedirs(os.path.dirname(spath), exist_ok=True)
        suid = existing_uid(spath) or new_uid(sres)
        open(spath, "w", newline="\n").write(SCENE.format(uid=suid, script_uid=item_uid, script=ITEM_SCRIPT, model_uid=model_uid, model=model,
                                                          id=iid, shape=shape_text(shape), physics=physics_text(phys),
                                                          tags=", ".join(str(t) for t in tags)))
        icon = f"res://game/assets/icons/items/{iid}.png"
        tpath = os.path.join(REPO, "game", "definitions", GROUP_DIRS[group], iid + ".tres")
        os.makedirs(os.path.dirname(tpath), exist_ok=True)
        desc = behaviour.replace('"', "'")
        open(tpath, "w", newline="\n").write(TRES.format(info_uid=info_uid, info=INFO_SCRIPT, icon_uid=icon_uid(icon), icon=icon,
                                                         scene_uid=suid, scene=sres, id=iid, name=name, desc=desc))
    print(f"{len(ITEMS)} items written")

if __name__ == "__main__":
    main()
