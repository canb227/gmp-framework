// Full-screen backdrop for in-game menus (pause menu): blurs and darkens whatever is drawn behind it, the game and
// the HUD, so the menu reads clearly on top. Blur comes from a lower mip level of the screen, smoothed with a 3x3
// tent filter so the mip's blocks don't show.
shader_type canvas_item;

uniform sampler2D screen_texture : hint_screen_texture, filter_linear_mipmap;
uniform float blur_lod : hint_range(0.0, 6.0) = 3.5;
uniform float darken : hint_range(0.0, 1.0) = 0.55;
uniform vec4 tint : source_color = vec4(0.012, 0.012, 0.016, 1.0);

void fragment() {
	vec2 step_uv = SCREEN_PIXEL_SIZE * exp2(blur_lod);
	vec3 sum = vec3(0.0);
	float weight = 0.0;
	for (int x = -1; x <= 1; x++) {
		for (int y = -1; y <= 1; y++) {
			float w = (2.0 - abs(float(x))) * (2.0 - abs(float(y)));
			sum += textureLod(screen_texture, SCREEN_UV + vec2(float(x), float(y)) * step_uv, blur_lod).rgb * w;
			weight += w;
		}
	}
	COLOR = vec4(mix(sum / weight, tint.rgb, darken), 1.0);
}
