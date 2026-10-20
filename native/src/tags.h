#pragma once

#include <cstdint>

// Tags: what a thing is, one bit each. Bit n is the tag numbered n in the game's ItemTags enum
// (game/scripts/items/ItemTags.cs), so those numbers must never change. Where one tag must be picked from
// several, the lowest wins.
//
// Shapes, items and bodies carry tags. A Box3D shape keeps its tags (its own plus its body's) in its surface
// material's userMaterialId, so every hit, ray cast and friction callback that names the shape carries them too.

// The tag names and bit values for the inspector's tick boxes: keep them the same as ItemTags in C#.
const char *const TagHintString = "PLAYER:1,METAL:2,ROCK:4,CONCRETE:8,PLASTIC:16,GLASS:32,WOOD:64,RUBBER:128";

// A player's body (on its GMPCharacter). Hits with a player in them have it as side A, and play on arrival on
// other peers (players are shown at their newest state there).
const uint64_t PlayerTag = 1ull << 0;
