#version 450

layout(set = 2, binding = 0) uniform sampler2D mainTex;

layout(location = 0) in vec2 fTex;

layout(location = 0) out vec3 outColor;

void main() {
    outColor = texture(mainTex, fTex).rgb;
}