#version 450

layout(set = 2, binding = 0) uniform sampler2D mainTex;

layout(location = 0) in vec2 fTex;
layout(location = 1) in vec4 fCol;
layout(location = 2) in vec4 fOCol;

layout(location = 0) out vec4 outColor;

void main() {
    outColor = (texture(mainTex, fTex) * fCol) + fOCol;
}