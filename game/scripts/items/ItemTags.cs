using PolyType;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


/// <summary>
/// Properties of a physical item that drive interactions between items (see <see cref="TagInteractions"/>).
/// Stored in scenes by value, so never renumber existing entries.
/// </summary>
public enum ItemTags
{
    INERT = 0,
    HOT = 1,
    COLD = 2,
    // Surface materials: what an item or structure sounds like when struck (see ImpactSounds). An object's first
    // material tag is its surface; a collision shape's user_material_id may hold one of these values instead.
    METAL = 3,
    ROCK = 4,
    RUBBER = 5,
    CONCRETE = 6,
    PLASTIC = 7,
    GLASS = 8,
    // Behaviour tags for the resources and products (see TagInteractions for what each pair does).
    FUEL = 9,          // burns when it touches something HOT (and becomes HOT itself)
    FRAGILE = 10,      // shatters on hard impacts (impact-speed rule, not a tag pair)
    STICKY = 11,       // glues itself to whatever it touches; COLD hardens it
    MAGNETIC = 12,     // pulls FERROUS items (and other magnets) toward itself; HOT switches it off
    VOLATILE = 13,     // detonates on HOT or CHARGED contact
    SOLUBLE = 14,      // dissolves when WET; melts COLD items it touches
    WET = 15,          // soaked (quench tank, melted frost)
    SLIPPERY = 16,     // near-frictionless: belts can't carry it (physical, via friction)
    BUOYANT = 17,      // floats in liquids (water, magma), drifts in fans
    LIQUID = 18,       // beads merge with other LIQUID items; amalgamate with CONDUCTIVE metal
    CHARGED = 19,      // repels other CHARGED items, discharges into CONDUCTIVE ones, sparks VOLATILE ones
    CONDUCTIVE = 20,   // grounds CHARGED items
    INSULATED = 21,    // blocks HOT / COLD / CHARGED interactions through it
    FERROUS = 22,      // attracted by MAGNETIC items and magnetic belts
}
