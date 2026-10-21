#pragma once

#include <cstdint>

// Tags: data about a body or one of its shapes, one bit each, for whoever reads hits, ray casts and touches to
// interpret as they wish. Bit n is the tag numbered n in the game's SurfaceTags enum
// (game/scripts/items/SurfaceTags.cs), so those numbers must never change.
//
// Shapes, items and bodies carry tags. A Box3D shape keeps its tags (its own plus its body's) in its surface
// material's userMaterialId, so every hit, ray cast and friction callback that names the shape carries them too.

// The tag names and bit values for the inspector's tick boxes: keep them the same as SurfaceTags in C#.
const char *const TagHintString = "PLAYER:1,METAL:2,ROCK:4,CONCRETE:8,PLASTIC:16,GLASS:32,WOOD:64,RUBBER:128,LEVEL:256,STRUCTURE:512,BREAKABLE:1024,TOOL:2048";

// A player's body (on its GMPCharacter). Hits with a player in them have it as side A, and play on arrival on
// other peers (players are shown at their newest state there).
const uint64_t PlayerTag = 1ull << 0;
// The level's own geometry and fixtures, a structure's bodies, and a dropped tool's body. With PlayerTag they set
// which side of a hit is side A (hit_side_rank).
const uint64_t LevelTag = 1ull << 8;
const uint64_t StructureTag = 1ull << 9;
const uint64_t ToolTag = 1ull << 11;
