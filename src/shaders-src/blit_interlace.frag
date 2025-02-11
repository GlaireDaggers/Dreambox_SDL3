#version 450

#define VERTICAL_RES 480

layout(set = 2, binding = 0) uniform sampler2D curTex;
layout(set = 2, binding = 1) uniform sampler2D prevTex;

layout(location = 0) in vec2 fTex;

layout(location = 0) out vec3 outColor;

layout(std140, set = 3, binding = 0) uniform UBO {
    int frame;
} ubo;

void main() {
    int pix_y = int(fTex.y * VERTICAL_RES);
    bool sel = (pix_y % 2) == 0;

    float darken = ((pix_y + ubo.frame) % 2) == 0 ? 1.25 : 0.75;

    outColor = mix(texture(prevTex, fTex), texture(curTex, fTex), sel.xxxx).rgb * darken;
}