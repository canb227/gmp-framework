// The highlighted placement face (BuildGrid.Reveal.cs): a cell-sized quad on the face the ghost is snapped to,
// with a faint fill, a soft rounded border inset from the cell's edges, and a slow breathing pulse.
shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled, shadows_disabled, fog_disabled;

uniform vec4 face_color : source_color = vec4(0.7, 0.9, 1.0, 1.0);
uniform float opacity : hint_range(0.0, 1.0) = 1.0;
uniform float fill_alpha = 0.05;
uniform float border_alpha = 0.32;
// In UV units (0-1 across the face).
uniform float border_inset = 0.06;
uniform float border_width = 0.025;
uniform float border_softness = 0.05;
uniform float corner_radius = 0.1;
uniform float pulse_speed = 1.6;
uniform float pulse_depth = 0.25;

// Signed distance to a rounded square of half-size b and corner radius r, centred on the origin.
float rounded_box(vec2 p, vec2 b, float r) {
	vec2 q = abs(p) - b + r;
	return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
}

void fragment() {
	vec2 p = UV - 0.5;
	float sd = rounded_box(p, vec2(0.5 - border_inset), corner_radius);
	float border = 1.0 - smoothstep(border_width, border_width + border_softness, abs(sd));
	// The fill brightens a touch toward the border, so the face reads as a lit panel rather than a flat tint.
	float inside = 1.0 - smoothstep(-border_softness, 0.0, sd);
	float fill = inside * mix(0.6, 1.0, smoothstep(-0.4, 0.0, sd));
	float pulse = 1.0 - pulse_depth + pulse_depth * (0.5 + 0.5 * sin(TIME * pulse_speed * 6.2832 * 0.5));

	ALBEDO = face_color.rgb;
	ALPHA = clamp((fill * fill_alpha + border * border_alpha * pulse) * face_color.a * opacity, 0.0, 1.0);
}
