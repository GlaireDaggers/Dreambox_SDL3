#version 450

layout(location = 0) in vec2 fTex;
layout(location = 0) out vec4 outColor;

layout(set = 2, binding = 0) uniform sampler2D screen_texture;

layout(std140, set = 3, binding = 0) uniform UBO {
    vec2 output_resolution;
    float c;
} ubo;

void main() {
    float luma = 0.0;

	// run a simple box filter over carrier wavelength to get luma from input
	for (float i = 0.0; i < ubo.c; i++) {
		vec2 offs = fTex + (vec2(float(i) / ubo.output_resolution.x, 0.0) * 2.0);
		vec2 sig = texture(screen_texture, offs).gb * 2.0 - 1.0;
		luma += sig.x;
	}

	luma /= ubo.c;

	// subtract extracted luma from signal to get chroma
	vec2 chr = (texture(screen_texture, fTex).gb * 2.0 - 1.0) - luma;

	// returning three channels - one with luma, and two with cur+prev chroma
	outColor = vec4(luma, chr * 0.5 + 0.5, 1.0);
}