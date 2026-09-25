shader_type spatial;

uniform sampler2D source_tex : source_color, filter_nearest;
uniform float scroll_speed = -1.0;

void fragment() {
    vec2 uv = UV;
    uv.y += TIME * scroll_speed;
    ALBEDO = texture(source_tex, fract(uv)).rgb;
}