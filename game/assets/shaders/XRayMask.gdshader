// Pass 1 of the through-walls outline (XRayHighlight.cs): marks the object's whole silhouette in the stencil buffer,
// ignoring depth so occluded parts count too. Draws nothing visible. Its next pass is XRayOutline.gdshader.
shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, depth_test_disabled, cull_disabled, shadows_disabled, fog_disabled;
stencil_mode write, compare_always, 128;

void fragment() {
	ALBEDO = vec3(0.0);
	ALPHA = 0.0;
}
