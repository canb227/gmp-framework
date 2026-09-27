shader_type spatial;
render_mode blend_mix, depth_draw_always;

uniform sampler2D screen_texture : hint_screen_texture, filter_linear_mipmap_anisotropic;

group_uniforms Color;
uniform vec3 color : source_color = vec3(1.0);
uniform float color_coat : hint_range(0.0, 1.0, 0.01) = 0.0;
uniform float oilyness : hint_range(0.0, 2.0, 0.01) = 0.1;

group_uniforms Rim;
uniform float rim_amount : hint_range(0.0, 1.0, 0.01) = 0.4;
uniform float rim_shape : hint_range(0.1, 6.0, 0.01) = 4.0;
uniform vec3 rim_color : source_color = vec3(1.0);

group_uniforms Refraction;
uniform float refraction_strength : hint_range(-1.0, 1.0, 0.01) = -.1;

group_uniforms Blur;
uniform float blur_amount: hint_range(0.1, 8.0) = 4.0;
uniform float roughness_affect : hint_range(0.0, 1.0, 0.01) = 0.0;

group_uniforms Roughness;
uniform float roughness : hint_range(0.0, 1.0, 0.01) = 0.2;
uniform sampler2D roughness_texture;

float overlay(float base, float blend){
	float limit = step(0.5, base);
	return mix(2.0 * base * blend, 1.0 - 2.0 * (1.0 - base) * (1.0 - blend), limit);
}

float gaussian_distribution(float x, float STD){ // STD stands for standard deviation
	return exp(-(x*x)/(2.*STD*STD))/(sqrt(2.*PI)*STD);
}

vec3 gaussian_blur(sampler2D tex, vec2 pos, vec2 pixel_size, float sigmaUsed, int radius){
    vec3 blurredPixel = vec3(0.0);
    float total_weight = 0.0;
	// Loop over the radius (tecnically its a square)
    for(int i = -radius ; i <= radius; i++){ 
        for(int j = -radius; j <= radius; j++){
			// Calculate the offset from the current pixel
			vec2 offset = vec2(float(i), float(j))*pixel_size * 4.0;
			vec2 changedPos = pos + offset;        
			
			// Calculate the weight based on the Gaussian distribution multiplying both dimentions (how far are X and Y form the center (pos))
			float weight = gaussian_distribution(float(i), sigmaUsed)*gaussian_distribution(float(j), sigmaUsed);
			// Add the weighted color value to the blurred pixel
			vec3 sample = textureLod(tex, changedPos, 2.0).rgb;
			blurredPixel += sample * weight;
			total_weight += weight;
		}
	}
	// Normalize the blurred pixel color by the total weight
	blurredPixel/=total_weight;
    return blurredPixel;
}

void fragment() {
	vec2 pixel_size = 1.0 / vec2(textureSize(screen_texture, 0));
	
	float fresnel = pow(clamp(1.0 - dot(NORMAL, VIEW),0.0, 1.0), 1.0);
	vec2 offset = vec2(NORMAL.x, -NORMAL.y) * fresnel;
	
	vec2 screen_uv = SCREEN_UV + offset * refraction_strength;
	
	float roughness_value = clamp(texture(roughness_texture, UV).r * roughness, 0.01, 1.0);
	
	float blur = mix(blur_amount, overlay(blur_amount, 1.0 - roughness_value), roughness_affect);
	vec3 glass = gaussian_blur(screen_texture, screen_uv, pixel_size, blur, int(2.0 * (blur)));
	
	float coat_amount = pow(1.0 - color_coat, 1.0);
	
	ALBEDO = color * mix(vec3(1.0), glass, coat_amount);
	ALBEDO = mix(ALBEDO, rim_color, pow(fresnel, rim_shape) * rim_amount);
	
	ALBEDO -= vec3((sin(NORMAL.xy * 10.0) * 0.5 + 0.5) * (0.1 * oilyness), 0.0);
	
	EMISSION = ALBEDO * coat_amount;
	
	SPECULAR = 1.0;
	
	ROUGHNESS = roughness_value;
}

//void light() {
//	// Called for every pixel for every light affecting the material.
//	// Uncomment to replace the default light processing function with this one.
//}
