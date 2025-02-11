#version 450

#define M_PI 3.1415926535897932384626433832795

layout(location = 0) in vec2 fTex;
layout(location = 0) out vec4 outColor;

layout(set = 2, binding = 0) uniform sampler2D screen_texture;
layout(set = 2, binding = 1) uniform sampler2D cb_lut;

layout(std140, set = 3, binding = 0) uniform UBO {
    float c;
    bool temporal_blend;
    vec2 output_resolution;
} ubo;

vec3 yiq2rgb(vec3 yiq) {
	float r = yiq.x + 0.9469 * yiq.y + 0.6236 * yiq.z;
	float g = yiq.x - 0.2748 * yiq.y - 0.6357 * yiq.z;
	float b = yiq.x - 1.1000 * yiq.y + 1.7000 * yiq.z;

	return vec3(r, g, b);
}

void main() {
    vec2 uv = fTex;
	vec2 px_coord = ubo.output_resolution * fTex;

	float y = texture(screen_texture, fTex).r;

	float i, q;
	i = 0.0;
	q = 0.0;

	// colorburst phase
	vec2 phase = texture(cb_lut, vec2(0.0, uv.y), 0.0).rg + 0.05;

	// run a simple box filter over carrier wavelength to extract i/q
	for (float j = 0.0; j < ubo.c; j++) {
		vec2 offs = fTex + vec2(j / ubo.output_resolution.x);
		float x = px_coord.x + j;

		// colorburst
		float carrier1 = sin(2.0 * M_PI * (phase.x + x / ubo.c));
		float carrier2 = sin(2.0 * M_PI * (phase.y + x / ubo.c));
		float quadrature1 = -cos(2.0 * M_PI * (phase.x + x / ubo.c));
		float quadrature2 = -cos(2.0 * M_PI * (phase.y + x / ubo.c));

		vec2 smp = texture(screen_texture, offs).gb * 2.0 - 1.0;

		float i1 = smp.x * carrier1;
		float i2 = smp.y * carrier2;

		float q1 = smp.x * quadrature1;
		float q2 = smp.y * quadrature2;

		if (ubo.temporal_blend) {
			i += (i1 + i2) * 0.5;
			q += (q1 + q2) * 0.5;
		}
		else {
			i += i1;
			q += q1;
		}
	}

	i /= ubo.c;
	q /= ubo.c;

	// convert back to rgb
	vec3 rgb = yiq2rgb(vec3(y, i * 3.0, q * 3.0));

	outColor = vec4(rgb, 1.0);
}