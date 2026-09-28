// Field volumes of the antigravity / zero point projectors (and the powered chutes' glow shells): an additive
// shell, bright where it grazes the view, with bands drifting along the node's local -Z (the projection /
// flow direction). Box or tube meshes; no UVs needed, bands come from the model-space position.
shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_disabled, shadows_disabled;

uniform vec3 tint : source_color = vec3(0.55, 0.35, 1.0);
uniform float strength = 0.6;
uniform float band_spacing = 0.5;
uniform float flow_speed = 0.4;
uniform float rim_power = 2.0;

varying vec3 local_pos;

void vertex() {
	local_pos = VERTEX;
}

void fragment() {
	float facing = abs(dot(NORMAL, VIEW));
	float rim = pow(1.0 - facing, rim_power);
	float bands = pow(0.5 + 0.5 * sin((local_pos.z + TIME * flow_speed) / band_spacing * 6.2832), 6.0);
	float shimmer = 0.5 + 0.5 * sin(local_pos.x * 7.0 + local_pos.y * 5.0 + TIME * 1.3);
	ALBEDO = tint * strength * (0.12 + rim * 0.9 + bands * 0.35 + shimmer * 0.05);
}
