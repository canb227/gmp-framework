// The build grid revealed around a blueprint's ghost (BuildGrid.Reveal.cs). The mesh is a lattice of line
// segments: each segment is a quad whose four vertices sit on the line (VERTEX), with the line's direction in
// NORMAL and which side of the line in UV.y (-1/+1). The vertex shader widens it to a camera-facing ribbon a
// constant number of pixels wide; the fragment shader gives it soft edges and fades it out with distance from
// `focus`, so the grid seems to be lit up by the ghost. Lines on the structure-module grid (every major_size) are
// drawn bolder than the cell lines between them, and lattice nodes glint a little brighter. Everything here is
// fixed in the world or centred on a fixed focus: the grid must never appear to move.
shader_type spatial;
render_mode unshaded, skip_vertex_transform, blend_mix, depth_draw_never, cull_disabled, shadows_disabled, fog_disabled;

uniform vec4 line_color : source_color = vec4(0.66, 0.86, 1.0, 0.11);
uniform vec3 focus;
uniform float opacity : hint_range(0.0, 1.0) = 1.0;
// Fully lit within reveal_radius - fade_width of the focus, gone at reveal_radius; reveal_falloff steepens the fade
// (higher keeps the grid to a tighter pool around the ghost).
uniform float reveal_radius = 7.0;
uniform float reveal_falloff = 3.0;
uniform float fade_width = 5.75;
// Full-strength core width; the edges then feather out over edge_softness_px on each side.
uniform float line_width_px = 0.9;
uniform float edge_softness_px = 1.4;
uniform float cell_size = 1.0;
// Lines on the major_size grid are drawn at full strength, the cell lines between them at minor_strength.
uniform float major_size = 2.0;
uniform float minor_strength = 0.4;
// The cell lines fade out sooner than the module lines, over this fraction of the reveal radius.
uniform float minor_reveal_scale = 0.55;
uniform float node_size = 0.2;
uniform float node_boost = 0.45;
// Pulls the ribbon toward the camera so lines lying on a floor or wall aren't half buried in it.
uniform float camera_bias = 0.03;

varying vec3 world_pos;
varying float half_px;
varying float view_depth;
varying vec3 line_axis;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	line_axis = abs(NORMAL);
	vec3 p = (MODELVIEW_MATRIX * vec4(VERTEX, 1.0)).xyz;
	vec3 dir = normalize((MODELVIEW_MATRIX * vec4(NORMAL, 0.0)).xyz);
	vec3 to_cam = normalize(-p);
	vec3 across = cross(dir, to_cam);
	float len = length(across);
	// Looking straight down a line it collapses to a dot, which is what it should look like anyway.
	across = len > 1e-4 ? across / len : vec3(0.0);

	view_depth = -p.z;
	float px = 2.0 * max(view_depth, 0.01) / (PROJECTION_MATRIX[1][1] * VIEWPORT_SIZE.y);
	half_px = line_width_px * 0.5 + edge_softness_px + 0.5;
	p += across * UV.y * half_px * px;
	p += to_cam * camera_bias;
	VERTEX = p;
	NORMAL = to_cam;
}

void fragment() {
	float from_centre_px = abs(UV.y) * half_px;
	float coverage = 1.0 - smoothstep(line_width_px * 0.5 - 0.5, line_width_px * 0.5 + edge_softness_px, from_centre_px);

	float d = distance(world_pos, focus);
	float reveal = pow(1.0 - smoothstep(reveal_radius - fade_width, reveal_radius, d), reveal_falloff);
	float minor_radius = reveal_radius * minor_reveal_scale;
	float minor_reveal = pow(1.0 - smoothstep(minor_radius - fade_width * minor_reveal_scale, minor_radius, d), reveal_falloff);

	// Along a line two of the three coordinates sit on lattice planes; the largest offset from a plane is the
	// distance along the line to the nearest node.
	vec3 off = abs(fract(world_pos / cell_size + 0.5) - 0.5) * cell_size;
	float node = 1.0 - smoothstep(0.0, node_size, max(off.x, max(off.y, off.z)));

	// A line is major when both coordinates across it sit on the major grid (the one along it doesn't count).
	vec3 off_major = abs(fract(world_pos / major_size + 0.5) - 0.5) * major_size * (1.0 - line_axis);
	float major = step(max(off_major.x, max(off_major.y, off_major.z)), 0.25 * cell_size);
	float weight = mix(minor_strength * minor_reveal, reveal, major);

	// Don't let lines right in front of the camera clutter the view.
	float near = smoothstep(0.25, 1.2, view_depth);

	ALBEDO = line_color.rgb * (1.0 + node * node_boost * 0.5);
	ALPHA = clamp(line_color.a * weight * coverage * near * opacity * (1.0 + node * node_boost), 0.0, 1.0);
}
