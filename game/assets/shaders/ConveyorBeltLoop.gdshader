// Rubber belt for the salvage conveyor models (game/assets/models/conveyors).
// The belt mesh is one closed loop whose UV.y is metres of belt around the loop, so scrolling it moves the
// top run forward and the exposed return run underneath backward. Every run is a whole number of rib_pitch
// long, so the ribs line up across cell seams.
shader_type spatial;

/// Belt surface speed in m/s; positive carries items toward the model's front on the top run.
instance uniform float belt_speed = 2.0;
uniform float rib_pitch = 0.25;
uniform vec3 rubber_color : source_color = vec3(0.055, 0.055, 0.058);
uniform vec3 rib_color : source_color = vec3(0.09, 0.09, 0.095);
uniform vec3 dust_color : source_color = vec3(0.32, 0.29, 0.25);
uniform float dust = 0.35;

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

void fragment() {
	// metres of belt, fixed to the rubber (glTF export stores v flipped, so negate it back to Blender's v)
	float m = -UV.y - TIME * belt_speed;
	float f = fract(m / rib_pitch);
	float r = rib(f);
	// dust ground into the rubber along the belt, heavier toward the edges
	float streak = vnoise(vec2(UV.x * 38.0, m * 0.6)) * vnoise(vec2(UV.x * 7.0, m * 2.3));
	float edge = smoothstep(0.38, 0.5, abs(UV.x - 0.5));
	float d = clamp(streak * dust * 1.6 + edge * 0.25 * dust, 0.0, 1.0);
	ALBEDO = mix(mix(rubber_color, rib_color, r), dust_color, d * (1.0 - r * 0.6));
	ROUGHNESS = mix(0.82, 0.62, r) + d * 0.1;
	float e = 0.01;
	float slope = (rib(fract(f + e)) - rib(fract(f - e))) / (2.0 * e);
	NORMAL_MAP = vec3(0.5, 0.5 + clamp(slope * 0.04, -0.35, 0.35), 1.0);
	NORMAL_MAP_DEPTH = 1.0;
}
