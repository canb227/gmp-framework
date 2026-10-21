// Full-screen overlay that makes a UI read as a physical display: faint scanlines, a slow refresh band and a
// vignette. Draws only darkening on top of the UI (mouse_filter ignore on the host ColorRect).
shader_type canvas_item;

uniform float line_count = 720.0;
uniform float line_strength : hint_range(0.0, 0.5) = 0.07;
uniform float vignette_strength : hint_range(0.0, 1.0) = 0.45;
uniform float band_strength : hint_range(0.0, 0.2) = 0.03;
uniform float band_speed = 0.08;

void fragment() {
	float lines = (sin(UV.y * line_count * 3.14159) * 0.5 + 0.5) * line_strength;
	float band = smoothstep(0.0, 0.08, 0.08 - abs(fract(UV.y - TIME * band_speed) - 0.5)) * band_strength;
	vec2 centered = UV - 0.5;
	float vignette = smoothstep(0.35, 0.85, length(centered * vec2(1.0, 0.85))) * vignette_strength;
	COLOR = vec4(0.0, 0.0, 0.0, clamp(lines + vignette - band, 0.0, 1.0));
}
