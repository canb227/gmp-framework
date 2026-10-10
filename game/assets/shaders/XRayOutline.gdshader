// Pass 2 of the through-walls outline (XRayHighlight.cs): the mesh grown outwards by a constant number of screen
// pixels, drawn over everything, but only outside the silhouette XRayMask.gdshader marked in the stencil. What's
// left is a ring around the object, visible through walls.
// Known limit: meshes with split normals (hard-edged boxes) open small notches at their corners.
shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, depth_test_disabled, cull_disabled, shadows_disabled, fog_disabled;
stencil_mode read, compare_not_equal, 128;

uniform vec4 color : source_color = vec4(1.0, 0.55, 0.05, 1.0);
uniform float width_px = 4.0;

void vertex() {
	vec4 clip = PROJECTION_MATRIX * (MODELVIEW_MATRIX * vec4(VERTEX, 1.0));
	vec3 view_normal = normalize((MODELVIEW_MATRIX * vec4(NORMAL, 0.0)).xyz);
	vec2 dir = (PROJECTION_MATRIX * vec4(view_normal, 0.0)).xy;
	float len = length(dir);
	if (len > 1e-5) {
		// Clip-space offset of width_px pixels: NDC spans 2 units per viewport size, scaled back up by w.
		clip.xy += dir / len * width_px * 2.0 / VIEWPORT_SIZE * clip.w;
	}
	POSITION = clip;
}

void fragment() {
	ALBEDO = color.rgb;
	ALPHA = color.a;
}
