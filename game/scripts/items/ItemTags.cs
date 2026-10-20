using System;

/// <summary>
/// What an item, structure, tool or one collider of them is made of and does, as bits: a thing has any number of them.
/// In the physics world every shape keeps its tags in its Box3D surface material, so hits, ray casts and touches carry
/// them; a shape's tags there are its own plus its body's (native/src/tags.h, which repeats these names and values).
/// Stored in scenes and resources by value, so never change an existing one's bit.
/// </summary>
[Flags]
public enum ItemTags : long
{
    None = 0,
    HOT = 1L << 1,
    COLD = 1L << 2,
    // Surface materials: what something sounds like when struck. The lowest one a shape has is its sound.
    METAL = 1L << 3,
    ROCK = 1L << 4,
    RUBBER = 1L << 5,
    CONCRETE = 1L << 6,
    PLASTIC = 1L << 7,
    GLASS = 1L << 8,
    // Behaviour tags for the resources and products (see TagInteractions for what each pair does).
    FUEL = 1L << 9,          // burns when it touches something HOT (and becomes HOT itself)
    FRAGILE = 1L << 10,      // shatters on hard impacts (impact-speed rule, not a tag pair)
    STICKY = 1L << 11,       // glues itself to whatever it touches; COLD hardens it
    MAGNETIC = 1L << 12,     // pulls FERROUS items (and other magnets) toward itself; HOT switches it off
    VOLATILE = 1L << 13,     // detonates on HOT or CHARGED contact
    SOLUBLE = 1L << 14,      // dissolves when WET; melts COLD items it touches
    WET = 1L << 15,          // soaked (quench tank, melted frost)
    SLIPPERY = 1L << 16,     // near-frictionless: belts can't carry it (physical, via friction)
    BUOYANT = 1L << 17,      // floats in liquids (water, magma), drifts in fans
    LIQUID = 1L << 18,       // beads merge with other LIQUID items; amalgamate with CONDUCTIVE metal
    CHARGED = 1L << 19,      // repels other CHARGED items, discharges into CONDUCTIVE ones, sparks VOLATILE ones
    CONDUCTIVE = 1L << 20,   // grounds CHARGED items
    INSULATED = 1L << 21,    // blocks HOT / COLD / CHARGED interactions through it
    FERROUS = 1L << 22,      // attracted by MAGNETIC items and magnetic belts
}
