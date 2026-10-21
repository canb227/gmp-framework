using System;

/// <summary>
/// Tags: data about a body or one of its shapes, as bits; a thing has any number of them. In the physics world every
/// shape keeps its tags in its Box3D surface material, so hits, ray casts and touches carry them: a shape's own plus its
/// body's (native/src/tags.h repeats these names and values: keep the two the same). What they mean is up to whoever
/// reads them.
/// <para>
/// Stored in scenes and resources by value, so never change an existing tag's bit. Bit 63 is never used (it is the sign
/// bit).
/// </para>
/// </summary>
[Flags]
public enum SurfaceTags : long
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
    LEVEL = 1L << 8,        // the level's own geometry and fixtures (tagged by hand in the level scene)
    STRUCTURE = 1L << 9,    // every body of a structure (tagged in each structure scene)
    BREAKABLE = 1L << 10,
    TOOL = 1L << 11,        // a dropped tool's body (tagged in each dropped tool scene)
}
