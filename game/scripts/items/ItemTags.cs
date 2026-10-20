using System;

/// <summary>
/// Tags: what an item, structure, tool, player or one collider of them is, as bits; a thing has any number of them. In
/// the physics world every shape keeps its tags in its Box3D surface material, so hits, ray casts and touches carry them:
/// a shape's own plus its body's (native/src/tags.h repeats these names and values: keep the two the same).
/// <para>
/// Where one tag must be picked from several (what a hit sounds like), the lowest wins. Stored in scenes and resources
/// by value, so never change an existing tag's bit. Bits 8 to 15 are kept for more materials, and bit 63 is never used
/// (it is the sign bit).
/// </para>
/// </summary>
[Flags]
public enum ItemTags : long
{
    None = 0,
    PLAYER = 1L << 0,       // a player's body: a hit with one in it has it as side A
    METAL = 1L << 1,
    ROCK = 1L << 2,
    CONCRETE = 1L << 3,
    PLASTIC = 1L << 4,
    GLASS = 1L << 5,
    WOOD = 1L << 6,
    RUBBER = 1L << 7,
}
