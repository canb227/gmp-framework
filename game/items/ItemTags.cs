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
}
