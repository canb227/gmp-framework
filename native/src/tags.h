#pragma once

#include <cstdint>

// Tags: what a thing is made of and what it does, one bit each. Bit n is the tag numbered n in the game's
// ItemTags enum (game/scripts/items/ItemTags.cs), so those numbers must never change.
//
// Shapes, items and bodies carry tags. A Box3D shape keeps its tags in its surface material's userMaterialId,
// so every hit, ray cast and friction callback that names the shape carries them too.

// The tag names and bit values for the inspector's tick boxes: keep them the same as ItemTags in C#.
const char *const TagHintString =
		"HOT:2,COLD:4,METAL:8,ROCK:16,RUBBER:32,CONCRETE:64,PLASTIC:128,GLASS:256,FUEL:512,FRAGILE:1024,"
		"STICKY:2048,MAGNETIC:4096,VOLATILE:8192,SOLUBLE:16384,WET:32768,SLIPPERY:65536,BUOYANT:131072,"
		"LIQUID:262144,CHARGED:524288,CONDUCTIVE:1048576,INSULATED:2097152,FERROUS:4194304";

// The surface tags, METAL to GLASS (bits 3 to 8): what something sounds like when struck. The lowest one a
// shape has is the one its sounds use.
const uint64_t SurfaceTags = 0x1F8;

// The tags a shape carries in the world: its own, plus the ones its object (item or body) has been given. The
// object's surface tags only reach shapes with no surface of their own, so a rubber belt on a metal conveyor
// stays rubber.
inline uint64_t combined_tags(uint64_t shapeTags, uint64_t objectTags) {
	if ((shapeTags & SurfaceTags) != 0) {
		objectTags &= ~SurfaceTags;
	}
	return shapeTags | objectTags;
}
