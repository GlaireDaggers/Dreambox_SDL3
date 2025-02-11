#version 450
layout (local_size_x = 1, local_size_y = 1, local_size_z = 1) in;

#define OP_NEVER       0x0200
#define OP_LESS        0x0201
#define OP_EQUAL       0x0202
#define OP_LEQUAL      0x0203
#define OP_GREATER     0x0204
#define OP_NOTEQUAL    0x0205
#define OP_GEQUAL      0x0206
#define OP_ALWAYS      0x0207

layout(set = 0, binding = 0) uniform sampler2D depthTexture;

layout(std430, set = 1, binding = 0) writeonly buffer OutputData {
    uint result;
} outputData;

layout (set = 2, binding = 0) uniform UBO {
    int rect_x;
    int rect_y;
    int rect_w;
    int rect_h;
    uint compareOp;
    float compareRef;
} ubo;

bool compare(float d) {
    switch (ubo.compareOp) {
        case OP_NEVER:      return false;
        case OP_LESS:       return ubo.compareRef < d;
        case OP_EQUAL:      return ubo.compareRef == d;
        case OP_LEQUAL:     return ubo.compareRef <= d;
        case OP_GREATER:    return ubo.compareRef > d;
        case OP_NOTEQUAL:   return ubo.compareRef != d;
        case OP_GEQUAL:     return ubo.compareRef >= d;
        case OP_ALWAYS:     return true;
    }

    return false;
}

void main() {
    uint passCount = 0;

    for (int j = 0; j < ubo.rect_h; j++) {
        for (int i = 0; i < ubo.rect_w; i++) {
            int x = i + ubo.rect_x;
            int y = j + ubo.rect_y;

            float d = texelFetch(depthTexture, ivec2(x, y), 0).r;
            if (compare(d)) {
                passCount++;
            }
        }
    }

    outputData.result = passCount;
}