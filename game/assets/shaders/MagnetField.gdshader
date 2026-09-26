// The magnet tool's capture ball: an additive force-field shell, bright at the rim, with field lines
// scrolling pole to pole. Meant for a SphereMesh (UV.y runs pole to pole).
shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_back;

uniform vec3 tint : source_color = vec3(0.3, 0.9, 1.0);
uniform float rim_power = 2.4;
uniform float line_count = 18.0;
uniform float line_speed = 5.0;

void fragment() {
	float facing = clamp(dot(NORMAL, VIEW), 0.0, 1.0);
	float rim = pow(1.0 - facing, rim_power);
	float lines = pow(0.5 + 0.5 * sin(UV.y * line_count * 6.2832 - TIME * line_speed), 8.0);
	float meridians = pow(0.5 + 0.5 * sin(UV.x * 12.0 * 6.2832 + TIME * 0.7), 16.0);
	ALBEDO = tint * (rim * 1.4 + (lines * 0.35 + meridians * 0.15) * (0.25 + rim) + 0.02);
}
