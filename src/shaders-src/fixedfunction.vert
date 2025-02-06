#version 450

layout(location = 0) in vec4 vPos;
layout(location = 1) in vec2 vTex;
layout(location = 2) in vec4 vCol;
layout(location = 3) in vec4 vOCol;

layout(location = 0) out vec2 fTex;
layout(location = 1) out vec4 fCol;
layout(location = 2) out vec4 fOCol;

layout(set = 1, binding = 0) uniform UBO {
    mat4 transform;
    mat4 llight;
    mat4 lcol;
} ubo;

void main() {
    gl_Position = ubo.transform * vPos;
    fTex = vTex;
    fCol = vCol;
    fOCol = vOCol;
}