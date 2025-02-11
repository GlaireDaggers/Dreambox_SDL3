#version 450

layout(location = 0) in vec2 fTex;
layout(location = 0) out vec4 outColor;

layout(std140, set = 3, binding = 0) uniform UBO {
    float screen_height;    // height of output LUT
    float fr;               // frame count
    float pps;              // phase offset per scanline
    float ppf;              // phase offset per frame
} ubo;

void main() {
    float prev_fr = ubo.fr - 1.0;

    float y = fTex.y * ubo.screen_height;
    float phase = (ubo.ppf * ubo.fr) + (ubo.pps * y);
    float prev_phase = (ubo.ppf * prev_fr) + (ubo.pps * y);

    outColor = vec4(mod(phase, 1.0), mod(prev_phase, 1.0), 0.0, 1.0);
}