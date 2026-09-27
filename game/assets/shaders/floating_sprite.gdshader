shader_type spatial;

render_mode blend_mix, cull_disabled;

uniform sampler2D albedo_tex : source_color;

uniform vec3 center = vec3(0.0, 0.0, 0.0);
uniform vec3 bounds = vec3(100.0, 50.0, 100.0);

uniform float speed = 0.25;
uniform float seed = 0.0;

vec3 wander(float t)
{
    return vec3(
        sin(t * 0.37 + seed * 2.1),
        sin(t * 0.53 + seed * 3.7),
        sin(t * 0.29 + seed * 1.4)
    );
}

void vertex()
{
    vec3 world_center =
        center ;

    vec3 camera_pos = CAMERA_POSITION_WORLD;

    vec3 forward =
        normalize(camera_pos - world_center);

    vec3 right =
        normalize(cross(vec3(0.0, 1.0, 0.0), forward));

    vec3 up =
        cross(forward, right);

    vec3 local =
        right * VERTEX.x +
        up * VERTEX.y;

    VERTEX = world_center + local;
}

void fragment()
{
    vec4 tex = texture(albedo_tex, UV);

    ALBEDO = tex.rgb;
    ALPHA = tex.a;
}