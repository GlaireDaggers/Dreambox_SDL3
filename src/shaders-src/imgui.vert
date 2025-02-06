#version 450

layout(location = 0) in vec2 vPos;
layout(location = 1) in vec2 vTex;
layout(location = 2) in vec4 vCol;

layout(location = 0) out vec2 fTex;
layout(location = 1) out vec4 fCol;

layout(std140, set = 1, binding = 0) readonly uniform UBO {
    mat4 proj;
};

void main() {
    gl_Position = vec4(vPos, 0.0, 1.0) * proj;
    
    fTex = vTex;
    fCol = vCol;
}