#version 450

layout(set = 2, binding = 0) uniform sampler2D screen_texture;

layout(location = 0) in vec2 fTex;

layout(location = 0) out vec4 outColor;

layout(std140, set = 3, binding = 0) uniform UBO {
    float sharpen_amount;
    float sharpen_resolution;
} ubo;

void main() {
    float neighbor = ubo.sharpen_amount * -1.0;
	float center = ubo.sharpen_amount * 2.0 + 1.0;

	vec2 offset = vec2(1.0 / ubo.sharpen_resolution, 0.0);

	vec3 rgb = texture(screen_texture, fTex - offset).rgb * neighbor +
		texture(screen_texture, fTex).rgb * center +
		texture(screen_texture, fTex + offset).rgb * neighbor;

	outColor = vec4(rgb, 1.0);
}