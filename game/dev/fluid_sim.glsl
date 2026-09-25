#[compute]
#version 450

layout(local_size_x = 32, local_size_y = 32) in;

// A binding to the uniforms we create in our script
layout(set = 0, binding = 0) uniform sampler2D noiseTexture;
layout(set = 0, binding = 1, r32f) uniform image2D outputImage;
layout(set = 0, binding = 2) restrict buffer ImageDimensions {
    int imageWidth;
    int imageHeight;
};

void main() {
    ivec2 coord = ivec2(gl_GlobalInvocationID.xy);
    vec2 uv = vec2(coord) / vec2(imageWidth, imageHeight);
    float height = texture(noiseTexture, uv).r;

    vec4 color = vec4(height, 0.0, 0.0, 1.0);

    imageStore(outputImage, coord, color);
}