// Floor of the item void: a slow spiral of violet and cyan light sinking into a black core.
// Expects UVs spanning the floor square (0..1 on both axes).
shader_type spatial;
render_mode unshaded;

uniform vec3 deep_color : source_color = vec3(0.004, 0.0, 0.012);
uniform vec3 outer_glow : source_color = vec3(0.42, 0.16, 1.0);
uniform vec3 inner_glow : source_color = vec3(0.2, 0.95, 1.0);
uniform float spin_speed = 0.9;
uniform float arms = 3.0;
uniform float intensity = 2.2;

float hash(vec2 p) {
	return fract(sin(dot(p, vec2(41.3, 289.1))) * 43758.5453);
}

void fragment() {
	vec2 p = UV * 2.0 - 1.0;
	float r = length(p);
	float a = atan(p.y, p.x);
	// log spiral, drifting inward over time
	float s = a * arms + log(r + 0.02) * 7.0 + TIME * spin_speed * 3.0;
	float bandv = pow(0.5 + 0.5 * sin(s), 6.0);
	float fine = pow(0.5 + 0.5 * sin(s * 3.0 + 1.3), 12.0) * 0.5;
	float fade = smoothstep(0.02, 0.35, r) * (1.0 - smoothstep(0.92, 1.18, r));
	// sparks: a sparse grid of motes streaming inward along the spiral
	vec2 cell = floor(vec2(a * 6.0, log(r + 0.02) * 10.0 + TIME * 2.5));
	float mote = step(0.965, hash(cell)) * fade;
	vec3 glow = mix(inner_glow, outer_glow, smoothstep(0.1, 0.8, r));
	ALBEDO = deep_color + glow * ((bandv + fine) * fade * intensity + mote * 1.5);
}
