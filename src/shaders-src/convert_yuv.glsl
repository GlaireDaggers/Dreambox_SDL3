#version 450
layout (local_size_x = 1, local_size_y = 1, local_size_z = 1) in;

layout(std430, set = 0, binding = 0) readonly buffer InputPlanes {
    uint imgdata[];
} inputPlanes;

layout(set = 1, binding = 0, rgba8) uniform writeonly image2D OutputTexture;

layout (set = 2, binding = 0) uniform UBO {
    uint imgwidth;
    uint imgheight;
} ubo;

uint loadByte(uint addr) {
    uint v = inputPlanes.imgdata[addr >> 2];
    uint shift = (addr & 3) << 3;

    return (v >> shift) & 0xFF;
}

vec3 yuv_to_rgb(vec3 yuv) {
    float y = yuv.x;
    float u = yuv.z - 128.0;
    float v = yuv.y - 128.0;

    float r = y + 45.0 * u / 32.0;
    float g = y - (11.0 * v + 23.0 * u) / 32.0;
    float b = y + 113.0 * v / 64.0;

    return vec3(r, g, b) / 255.0;
}

void main() {
    uint yPlaneSize = ubo.imgwidth * ubo.imgheight;
    uint uPlaneSize = (ubo.imgwidth >> 1) * (ubo.imgheight >> 1);

    uint uPlaneOffs = yPlaneSize;
    uint vPlaneOffs = uPlaneOffs + uPlaneSize;

    uint uStride = ubo.imgwidth >> 1;

    for (int j = 0; j < ubo.imgheight; j++) {
        for (int i = 0; i < ubo.imgheight; i++) {
            uint yAddr = i + (j * ubo.imgwidth);
            uint uAddr = (i >> 1) + ((j >> 1) * uStride);

            float y = float(loadByte(yAddr));
            float u = float(loadByte(uPlaneOffs + uAddr));
            float v = float(loadByte(vPlaneOffs + uAddr));

            vec3 rgb = yuv_to_rgb(vec3(y, u, v));

            imageStore(OutputTexture, ivec2(i, j), vec4(rgb, 1.0));
        }
    }
}