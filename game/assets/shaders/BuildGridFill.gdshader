// The cell fill shown with the build grid (BuildGrid.CellFill.cs): faint quads on the open faces of cells that can't
// be built in, coloured by what blocks them (the kind in UV.x: 0 level geometry, 1 structure, 2 no-build volume). It is
// lit by the grid's reveals (BuildGrid.Reveal.cs), one light per reveal: each fades with distance just as the grid
// lines do, and faces turned away from it keep only `ambient` of its light, which shades the cells as blocks.
// Overlapping lights unite like the overlapping grid reveals do (1 - (1-a)(1-b)).
shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_back, shadows_disabled, fog_disabled;

uniform vec4 level_color : source_color = vec4(0.66, 0.86, 1.0, 0.09);
uniform vec4 structure_color : source_color = vec4(1.0, 0.82, 0.5, 0.09);
uniform vec4 no_build_color : source_color = vec4(1.0, 0.32, 0.25, 0.11);
// One light per reveal (BuildGrid's RevealLayers): xyz where it is, w how strongly it shows.
uniform vec4 lights[5];
uniform float light_radii[5];
// The same fade as BuildGridReveal.gdshader's lines.
uniform float fade_width = 5.75;
uniform float reveal_falloff = 3.0;
// What a face turned away from a light keeps of it.
uniform float ambient = 0.35;
// Lifts the faces off the surfaces they lie on.
uniform float lift = 0.01;

varying vec3 world_pos;
varying vec3 world_normal;
varying float kind;

void vertex() {
	VERTEX += NORMAL * lift;
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
	kind = UV.x;
}

void fragment() {
	float unlit = 1.0;
	for (int i = 0; i < 5; i++) {
		vec3 to_light = lights[i].xyz - world_pos;
		float d = length(to_light);
		float reveal = pow(1.0 - smoothstep(light_radii[i] - fade_width, light_radii[i], d), reveal_falloff);
		float facing = max(dot(world_normal, to_light / max(d, 0.001)), 0.0);
		unlit *= 1.0 - lights[i].w * reveal * mix(ambient, 1.0, facing);
	}

	vec4 color = level_color;
	if (kind > 1.5) {
		color = no_build_color;
	} else if (kind > 0.5) {
		color = structure_color;
	}
	ALBEDO = color.rgb;
	ALPHA = color.a * (1.0 - unlit);
}
