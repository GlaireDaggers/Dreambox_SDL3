#version 450

layout(location = 0) in vec2 vPos;
layout(location = 1) in vec2 vTex;

layout(location = 0) out vec2 fTex;

void main() {
    gl_Position = vec4(vPos, 0.0, 1.0);
    fTex = vTex;
}