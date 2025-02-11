#version 450

#define M_PI 3.1415926535897932384626433832795

layout(location = 0) in vec2 fTex;
layout(location = 0) out vec4 outColor;

layout(set = 2, binding = 0) uniform sampler2D screen_texture;
layout(set = 2, binding = 1) uniform sampler2D cb_lut;

layout(std140, set = 3, binding = 0) uniform UBO {
    bool svideo;
    float noise_amount;
    float time;
    float c;
    vec2 output_resolution;
} ubo;

vec3 rgb2yiq(vec3 rgb) {
	float y = 0.30 * rgb.r + 0.59 * rgb.g + 0.11 * rgb.b;
	float i = (-0.27 * (rgb.b - y)) + (0.74 * (rgb.r - y));
	float q = (0.41 * (rgb.b - y)) + (0.48 * (rgb.r - y));

	return vec3(y, i, q);
}

float rand(vec2 co){ return fract(sin(dot(co.xy ,vec2(12.9898,78.233))) * 43758.5453); }

void main() {
    vec2 uv = fTex;
	vec2 px_coord = ubo.output_resolution * uv;
	vec2 scr_coord = px_coord / vec2(ubo.c, 1.0);

    vec3 rgb = texture(screen_texture, uv).rgb;
    vec3 yiq = rgb2yiq(rgb);

    // colorburst
	vec2 phase = texture(cb_lut, vec2(0.0, uv.y)).rg;
	float carrier1 = sin(2.0 * M_PI * (phase.x + px_coord.x / ubo.c));
	float carrier2 = sin(2.0 * M_PI * (phase.y + px_coord.x / ubo.c));
	float quadrature1 = -cos(2.0 * M_PI * (phase.x + px_coord.x / ubo.c));
	float quadrature2 = -cos(2.0 * M_PI * (phase.y + px_coord.x / ubo.c));

    // sample noise
	float noise = (rand(fTex + mod(ubo.time, 1.0)) * 2.0 - 1.0) * ubo.noise_amount;

    float luma = yiq.x;
	float chroma1 = carrier1 * yiq.y + quadrature1 * yiq.z;
	float chroma2 = carrier2 * yiq.y + quadrature2 * yiq.z;

    if (ubo.svideo) {
		float signal1 = chroma1 + noise;
		float signal2 = chroma2 + noise;

		outColor = vec4(luma + noise, signal1 * 0.5 + 0.5, signal2 * 0.5 + 0.5, 1.0);
	}
	else {
		float signal1 = (luma + chroma1) + noise;
		float signal2 = (luma + chroma2) + noise;

		outColor = vec4(0.0, signal1 * 0.5 + 0.5, signal2 * 0.5 + 0.5, 1.0);
	}
}