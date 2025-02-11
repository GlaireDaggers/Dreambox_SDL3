#version 450

layout(set = 2, binding = 0) uniform sampler2D mainTex;
layout(set = 2, binding = 1) uniform sampler2D shadowMask;

layout(location = 0) in vec2 fTex;

layout(location = 0) out vec4 outColor;

layout(std140, set = 3, binding = 0) uniform UBO {
    vec2 curvature;
    vec2 scale;
    vec2 maskScale;
} ubo;

void main() {
    // tv distortion
    vec2 uv = fTex * 2.0 - 1.0;
	uv *= ubo.scale;

	vec2 center_uv;
	center_uv.x = (1.0 - uv.y * uv.y) * ubo.curvature.x * uv.x;
	center_uv.y = (1.0 - uv.x * uv.x) * ubo.curvature.y * uv.y;

	uv = (uv - center_uv) * 0.5 + 0.5;

    // shadow mask
	vec3 mask = texture(shadowMask, uv * ubo.maskScale).rgb * 0.4 + 0.8;

    // border mask
    float border = smoothstep(0.0, 0.05, uv.x) * (1.0 - smoothstep(0.95, 1.0, uv.x))
        * smoothstep(0.0, 0.05, uv.y) * (1.0 - smoothstep(0.95, 1.0, uv.y));

    outColor = vec4(texture(mainTex, uv).rgb * smoothstep(0.1, 0.12, border) * mask, 1.0);
}