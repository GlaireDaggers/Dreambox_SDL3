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
    // in lighting mode, "color" is treated as packed normals (expanding 0..1 range to -1..1)
    vec3 nrm = vCol.rgb * 2.0 - 1.0;

    // very similar to PS1: normal vector is first transformed by "local light matrix", clamped, then transformed by "local color matrix"
    // representation allows for up to three directional lights plus ambient to be represented - or, alternatively, SH lighting if you're clever
    vec3 light = clamp((ubo.llight * vec4(nrm, 1.0)).rgb, 0.0, 1.0);
    vec3 col = clamp((ubo.lcol * vec4(light, 1.0)).rgb, 0.0, 1.0);

    gl_Position = ubo.transform * vPos;
    fTex = vTex;
    fCol = vec4(col, 1.0);
    fOCol = vOCol;
}