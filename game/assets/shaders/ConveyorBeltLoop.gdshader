// Rubber belt for the salvage conveyor models (game/assets/models/conveyors).
// The belt mesh is one closed loop whose UV v is metres of belt around the loop: the top run from 0 at the back
// to L at the front, a wrap of half a rib pitch at the front face, the return run back underneath, and a wrap
// at the back face, closing at 2L + rib_pitch. Scrolling v moves the top run forward and the exposed return run
// underneath backward. Every L is a whole number of rib_pitch, so the ribs line up across cell seams.
//
// Pieces in a belt line (BuildGrid.Belts.cs) are drawn as one continuous belt:
// - belt_length and belt_offset (their distance along the line) pattern the belt by line position instead of the
//   piece's own v, so the dust streaks flow unbroken from piece to piece, on the return run too. A piece whose
//   model runs against the flow (negative belt_speed, e.g. the down slope) or is a mirrored build
//   (belt_mirror_u, the left turns) still patterns along the flow, the same way round.
// - At an end joined to another belt (belt_join_entry / belt_join_exit) the wrap is pressed flat into the top and
//   return runs, so the two pieces meet as one surface instead of two rounded edges.
// - In a closed loop (belt_loop_length) the pattern repeats exactly once around it, so the loop has no break.
// Without belt_length (previews, loose models) each piece is drawn on its own.
shader_type spatial;

/// Belt surface speed in m/s; positive carries items toward the model's front on the top run, negative means the
/// model is turned against the flow.
instance uniform float belt_speed = 2.0;
/// The model's u runs right to left across its flow (a mirrored build, like the left turns).
instance uniform float belt_mirror_u = 0.0;
/// The top run's length in belt v (L above); 0 when the piece isn't in a belt line.
instance uniform float belt_length = 0.0;
/// Distance along the belt line to where this piece's top run starts.
instance uniform float belt_offset = 0.0;
/// Total length of the closed loop this piece is part of; 0 for an open line.
instance uniform float belt_loop_length = 0.0;
/// 1 where another belt carries on from this piece's entry / exit.
instance uniform float belt_join_entry = 0.0;
instance uniform float belt_join_exit = 0.0;

uniform float rib_pitch = 0.25;
/// The models' wrap radius and top-to-return spacing (BELT_RC and BELT_H in build_conveyors.py).
uniform float wrap_radius = 0.006;
uniform float return_depth = 0.085;
/// How far a joined end's surface reaches past the cell face, to overlap its neighbour's.
uniform float joint_overlap = 0.002;
uniform vec3 rubber_color : source_color = vec3(0.055, 0.055, 0.058);
uniform vec3 rib_color : source_color = vec3(0.09, 0.09, 0.095);
uniform vec3 dust_color : source_color = vec3(0.32, 0.29, 0.25);
uniform float dust = 0.35;

varying float belt_v;

float hash(vec2 p) {
	return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

float vnoise(vec2 p) {
	vec2 i = floor(p), f = fract(p);
	vec2 u = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1, 0)), u.x), mix(hash(i + vec2(0, 1)), hash(i + vec2(1, 1)), u.x), u.y);
}

float rib(float f) {
	return smoothstep(0.0, 0.05, f) * (1.0 - smoothstep(0.13, 0.18, f));
}

float streak(float u, float m) {
	return vnoise(vec2(u * 38.0, m * 0.6)) * vnoise(vec2(u * 7.0, m * 2.3));
}

bool reversed() {
	return belt_speed < 0.0;
}

void vertex() {
	// glTF export stores v flipped (v -> 1 - v); flip it back to Blender's v
	belt_v = 1.0 - UV.y;
	if (belt_length > 0.0) {
		// Each wrap, front (just past L) and back (closing the loop), is a quarter arc down from the top run to the
		// cell face, a straight drop, and a quarter arc into the return run, its v spread over it by length. At a
		// joined end press the top arc up into the top run's plane and the bottom arc down into the return run's:
		// the drop between them is left standing at the face, hidden between the runs.
		float L = belt_length, cap = 0.5 * rib_pitch;
		float chord = 2.0 * wrap_radius * sin(PI / 16.0);         // the arcs are four chords each
		float arc = 4.0 * chord;
		float wrap_len = 2.0 * arc + return_depth - 2.0 * wrap_radius;
		float join_back = reversed() ? belt_join_exit : belt_join_entry;   // the model's back is where v starts
		float join_front = reversed() ? belt_join_entry : belt_join_exit;
		// Distance round the wrap from the top run. The runs' last rows (s = 0 and s = wrap_len) are included: the
		// model is smooth shaded, so their normals lean into the wrap and would leave a shaded band at the joint.
		float eps = 1e-4;
		float s = -1.0, join = 0.0;
		if (belt_v >= L - eps && belt_v <= L + cap + eps) {
			s = clamp((belt_v - L) / cap, 0.0, 1.0) * wrap_len;
			join = join_front;
		} else if (belt_v >= 2.0 * L + cap - eps || belt_v <= eps) {
			float t = belt_v <= eps ? 1.0 : clamp((belt_v - 2.0 * L - cap) / cap, 0.0, 1.0);
			s = wrap_len - t * wrap_len;
			join = join_back;
		}
		if (join > 0.5 && s >= 0.0) {
			vec3 n0 = NORMAL;
			// The vertices at the cell face are nudged just past it (along the wrap's outward normal there), so the
			// neighbours' lips overlap a hair: meeting edge to edge, the two meshes (with different transforms)
			// leave pixel cracks along the joint.
			vec3 out_dir = vec3(n0.x, 0.0, n0.z);
			out_dir = length(out_dir) > 0.5 ? normalize(out_dir) : vec3(0.0);
			if (s <= arc + 1e-5) {
				float a = s / chord * PI / 8.0;
				VERTEX.y += wrap_radius * (1.0 - cos(a));
				VERTEX += out_dir * joint_overlap * step(PI / 2.0 - 0.01, a);
				NORMAL = vec3(0.0, 1.0, 0.0);
			} else if (s >= wrap_len - arc - 1e-5) {
				float a = (s - (wrap_len - arc)) / chord * PI / 8.0;
				VERTEX.y -= wrap_radius * (1.0 - sin(a));
				VERTEX += out_dir * joint_overlap * step(a, 0.01);
				NORMAL = vec3(0.0, -1.0, 0.0);
			}
			// TANGENT runs across the belt (along u) and is unchanged; turn the binormal to suit the new normal.
			float handed = sign(dot(cross(n0, TANGENT), BINORMAL));
			BINORMAL = handed * normalize(cross(NORMAL, TANGENT));
		}
	}
}

// Metres of belt at this fragment, fixed to the rubber: the coordinate the pattern is drawn in.
float belt_metres(float v) {
	if (belt_length <= 0.0) {
		return v - TIME * belt_speed;
	}
	float L = belt_length;
	float cap = 0.5 * rib_pitch;
	float loop_len = 2.0 * L + 2.0 * cap;
	v = mod(v, loop_len);
	if (v > 2.0 * L + 1.5 * cap) {
		v -= loop_len;                // the back wrap's top half leads into the top run
	}
	float m;
	if (v <= L + 0.5 * cap) {
		// top run (and the wraps' top halves): line position, increasing with the flow
		m = belt_offset + (reversed() ? L - v : v);
	} else {
		// return run: the same line positions, but it moves the other way, so count them backward
		m = -(belt_offset + (reversed() ? v - L - cap : 2.0 * L + cap - v));
	}
	return m - TIME * abs(belt_speed);
}

void fragment() {
	float m = belt_metres(belt_v);
	// u across the belt, left to right looking along the flow
	float u = UV.x;
	float dm_dv = 1.0;              // which way the pattern runs against the mesh's v (for the rib normals)
	float s;
	if (belt_length > 0.0) {
		if (reversed() != (belt_mirror_u > 0.5)) {
			u = 1.0 - u;
		}
		dm_dv = reversed() ? -1.0 : 1.0;
	}
	if (belt_loop_length > 0.0) {
		// Repeat once round the loop: fold the position into it, and fade the streaks into their own start over
		// the last stretch so the fold doesn't show.
		m = mod(m, belt_loop_length);
		float fade = min(1.0, 0.5 * belt_loop_length);
		float w = smoothstep(belt_loop_length - fade, belt_loop_length, m);
		s = mix(streak(u, m), streak(u, m - belt_loop_length), w);
	} else {
		s = streak(u, m);
	}
	float f = fract(m / rib_pitch);
	float r = rib(f);
	// dust ground into the rubber along the belt, heavier toward the edges
	float edge = smoothstep(0.38, 0.5, abs(u - 0.5));
	float d = clamp(s * dust * 1.6 + edge * 0.25 * dust, 0.0, 1.0);
	ALBEDO = mix(mix(rubber_color, rib_color, r), dust_color, d * (1.0 - r * 0.6));
	ROUGHNESS = mix(0.82, 0.62, r) + d * 0.1;
	float e = 0.01;
	float slope = dm_dv * (rib(fract(f + e)) - rib(fract(f - e))) / (2.0 * e);
	NORMAL_MAP = vec3(0.5, 0.5 + clamp(slope * 0.04, -0.35, 0.35), 1.0);
	NORMAL_MAP_DEPTH = 1.0;
}
