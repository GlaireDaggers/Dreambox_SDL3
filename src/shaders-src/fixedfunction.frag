#version 450

#define TC_NONE     0
#define TC_MUL      1
#define TC_ADD      2
#define TC_SUB      3
#define TC_MIX      4
#define TC_DOT3     5

layout(set = 2, binding = 0) uniform sampler2D tex0;
layout(set = 2, binding = 1) uniform sampler2D tex1;

layout(location = 0) in vec2 fTex0;
layout(location = 1) in vec2 fTex1;
layout(location = 2) in vec4 fCol;
layout(location = 3) in vec4 fOCol;

layout(location = 0) out vec4 outColor;

layout(set = 3, binding = 0) uniform UBO {
    uint tex_combine;
    uint vtx_combine;
} ubo;

vec4 combine(vec4 a, vec4 b, uint op) {
    switch (op) {
        case TC_NONE: return a;
        case TC_MUL: return a * b;
        case TC_ADD: return a + b;
        case TC_SUB: return a - b;
        case TC_MIX: return mix(a, b, b.w);
        case TC_DOT3: {
            return dot(a.xyz * 2.0 - 1.0, b.xyz * 2.0 - 1.0).xxxx;
        }
    }
}

void main() {
    vec4 tu0 = texture(tex0, fTex0);
    vec4 tu1 = texture(tex1, fTex1);
    vec4 tcol = combine(tu0, tu1, ubo.tex_combine);
    vec4 c = combine(tcol, fCol, ubo.vtx_combine);
    outColor = c + fOCol;
}