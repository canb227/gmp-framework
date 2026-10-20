#pragma once

#include "physics_body.h"

#include <cstdint>
#include <vector>

// A walking character (the player): a capsule moved by Box3D's character mover, which slides it along
// whatever it meets, plus a spring on a "legs" cast below it that holds it at standing height, so it
// glides up steps and stays on the ground walking down slopes.
//
// It is also a kinematic GMPBody with the same capsule, driven to wherever the mover puts the node, so
// loose items and other bodies are pushed by it and other characters bump into it. Loose items and
// dynamic bodies are soft for the mover (it presses into them a little while its body shoves them), so
// a character can wade through a pile instead of stopping dead.
//
// The node's origin is at the feet. The capsule spans from stepHeight above the feet to height; the
// legs cover the gap below it.
//
// C# decides the velocity (how walking feels, game/scripts/player/FactoryPlayer.cs) and calls move()
// once a physics tick; this file only does the collision.
class GMPCharacter : public GMPBody {
	GDCLASS(GMPCharacter, GMPBody)

public:
	float radius = 0.35f;
	float height = 1.8f;
	// How high a ledge the legs step up onto without jumping.
	float stepHeight = 0.4f;
	// Steeper ground than this doesn't hold the character up: it slides off.
	float maxSlopeDegrees = 50.0f;
	// How far below standing height the legs still find the ground, so walking down steps and slopes
	// keeps the character on the ground rather than launching it.
	float groundSnap = 0.25f;
	// How stiff the legs are (Hz): higher steps up quicker.
	float legHertz = 10.0f;

	GMPCharacter();

	// Moves the character by velocity (m/s) over delta seconds, sliding along what it meets, and
	// returns its velocity afterwards (what's left once walls and floors have taken their share).
	godot::Vector3 move(godot::Vector3 velocity, float delta);
	// Puts the character there at once.
	void teleport_character(godot::Transform3D transform);

	// As of the last move.
	bool is_on_floor() const;
	godot::Vector3 get_floor_normal() const;
	// How fast the ground under it moves (a moving body, or a belt's surface): standing still on it
	// means moving with it.
	godot::Vector3 get_floor_velocity() const;

	// Whether a capsule of this height would fit where the character is (standing up from a crouch).
	bool can_fit_height(float newHeight) const;

	void set_radius(float value);
	float get_radius() const;
	void set_height(float value);
	float get_height() const;
	void set_step_height(float value);
	float get_step_height() const;
	void set_max_slope_degrees(float value);
	float get_max_slope_degrees() const;
	void set_ground_snap(float value);
	float get_ground_snap() const;
	void set_leg_hertz(float value);
	float get_leg_hertz() const;

protected:
	static void _bind_methods();
	void _notification(int what);

private:
	b3ShapeId capsuleShape = b3_nullShapeId;
	float legVelocity = 0;
	bool onFloor = false;
	godot::Vector3 floorNormal = godot::Vector3(0, 1, 0);
	godot::Vector3 floorVelocity;
	// Where the legs found the floor, and on what.
	godot::Vector3 floorPoint;
	b3ShapeId floorShape = b3_nullShapeId;
	// The last move ended against a solid wall or ceiling: running along it doesn't count as running into it.
	bool againstWall = false;

	// The capsule, relative to the feet.
	b3Capsule capsule() const;
	// Makes (or remakes) the capsule on the body.
	void make_capsule_shape();
	// Casts the legs down from the capsule and sets the floor fields. distance: how far below standing
	// height the ground is (negative: above it).
	void probe_floor(b3WorldId world, b3Pos feet, bool rising, float &distance);
	// Something the move ran into: landing on the floor, or coming up against a wall or ceiling it wasn't against
	// before. It goes into the world's hits, with this character as side A. Soft things (items, dynamic bodies)
	// aren't reported here: Box3D's own hits are. normal: out of what it hit, toward the character.
	void add_impact(godot::Vector3 position, godot::Vector3 normal, float speed, b3ShapeId shape);
};
