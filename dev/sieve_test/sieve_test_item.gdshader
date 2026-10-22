// Sieve test items (dev/sieve_test): a soft two-tone colour, deeper at the item's own bottom and lighter at its top,
// so each size reads at a glance and you can see the item tumble.
shader_type spatial;

uniform vec3 bottom_color : source_color = vec3(0.6);
uniform vec3 top_color : source_color = vec3(0.9);
/// The item's height (a cube's edge, a ball's diameter), so the blend spans it.
uniform float size = 1.0;

varying float height;

void vertex() {
	// Mesh space, before the item's own turn: the gradient turns with it.
	height = clamp(VERTEX.y / size + 0.5, 0.0, 1.0);
}

void fragment() {
	ALBEDO = mix(bottom_color, top_color, smoothstep(0.0, 1.0, height));
	ROUGHNESS = 0.6;
}
