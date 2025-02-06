#version 450
layout (local_size_x = 1, local_size_y = 1, local_size_z = 1) in;

struct DrawCommand {
    uint num_vertices;
    uint num_instances;
    uint first_vertex;
    uint first_instance;
};

layout(std430, set = 1, binding = 0) buffer WorkBuffer {
   uint workmem[];
};

layout(std430, set = 1, binding = 1) buffer DrawDataBuffer {
   DrawCommand drawcmd;
};

layout(std430, set = 1, binding = 2) buffer VertexBuffer {
   uint vtxdata[];
};

void main() {
    // registers
    uint r[32];
    vec4 v[32];

    uint submit_addr = 0;
    uint submit_len = 0;

    uint pc = 0;
    while (pc < 64) {
        uint instr = workmem[pc++];

        uint op = instr & 0x3F;
        uint d = (instr >> 6) & 0x1F;
        uint s = (instr >> 11) & 0x1F;
        uint i = (instr >> 16) & 0xFFFF;
        uint sx = i & 3;
        uint sy = (i >> 2) & 3;
        uint sz = (i >> 4) & 3;
        uint sw = (i >> 8) & 3;

        switch (op) {
            case 0: {
                // ldui
                r[d] &= 0xFFFF;
                r[d] |= (i << 16);
                break;
            }
            case 1: {
                // ldli
                r[d] = i;
                break;
            }
            case 2: {
                // ldw
                r[d] = workmem[r[s]];
                break;
            }
            case 3: {
                // ldv
                float x = uintBitsToFloat(workmem[r[s]]);
                float y = uintBitsToFloat(workmem[r[s] + 1]);
                float z = uintBitsToFloat(workmem[r[s] + 2]);
                float w = uintBitsToFloat(workmem[r[s] + 3]);
                v[d] = vec4(x, y, z, w);
                break;
            }
            case 4: {
                // stw
                workmem[r[d]] = r[s];
                break;
            }
            case 5: {
                // stv
                vec4 vec = v[s];
                workmem[r[d]] = floatBitsToUint(vec.x);
                workmem[r[d] + 1] = floatBitsToUint(vec.y);
                workmem[r[d] + 2] = floatBitsToUint(vec.z);
                workmem[r[d] + 3] = floatBitsToUint(vec.w);
                break;
            }
            case 6: {
                // add
                r[d] += r[s];
                break;
            }
            case 7: {
                // sub
                r[d] -= r[s];
                break;
            }
            case 8: {
                // shl
                r[d] >>= r[s];
                break;
            }
            case 9: {
                // shr
                r[d] <<= r[s];
                break;
            }
            case 10: {
                // brz
                if (r[d] == 0) {
                    pc = i;
                }
                break;
            }
            case 11: {
                // brg
                if (r[d] > 0) {
                    pc = i;
                }
                break;
            }
            case 12: {
                // bge
                if (r[d] >= 0) {
                    pc = i;
                }
                break;
            }
            case 13: {
                // brl
                if (r[d] < 0) {
                    pc = i;
                }
                break;
            }
            case 14: {
                // ble
                if (r[d] <= 0) {
                    pc = i;
                }
                break;
            }
            case 15: {
                // bnz
                if (r[d] != 0) {
                    pc = i;
                }
                break;
            }
            case 16: {
                // addv
                v[d] += v[s];
                break;
            }
            case 17: {
                // subv
                v[d] -= v[s];
                break;
            }
            case 18: {
                // mulv
                v[d] *= v[s];
                break;
            }
            case 19: {
                // divv
                v[d] /= v[s];
                break;
            }
            case 20: {
                // sqrt
                v[d] = sqrt(v[s]);
                break;
            }
            case 21: {
                // abs
                v[d] = abs(v[s]);
                break;
            }
            case 22: {
                // sign
                v[d] = sign(v[s]);
                break;
            }
            case 23: {
                // sum
                v[d][sx] = dot(v[s], vec4(1.0, 1.0, 1.0, 1.0));
                break;
            }
            case 24: {
                // shfv
                vec4 vec = v[s];
                v[d] = vec4(vec[sx], vec[sy], vec[sz], vec[sw]);
                break;
            }
            case 25: {
                // dot
                vec4 vec = v[d];
                v[d] = vec4(0.0, 0.0, 0.0, 0.0);
                v[d][sx] = dot(vec, v[s]);
                break;
            }
            case 26: {
                // mulm
                vec4 col0 = v[s];
                vec4 col1 = v[s + 1];
                vec4 col2 = v[s + 2];
                vec4 col3 = v[s + 3];
                mat4 m = mat4(col0, col1, col2, col3);
                v[d] = m * v[d];
                break;
            }
            case 27: {
                // tocl
                vec4 vec = clamp(v[s], 0.0, 1.0);
                uint cr = uint(vec.x * 255.0);
                uint cg = uint(vec.y * 255.0);
                uint cb = uint(vec.z * 255.0);
                uint ca = uint(vec.w * 255.0);
                r[d] = cr | (cg << 8) | (cb << 16) | (ca << 24);
                break;
            }
            case 28: {
                // frcl
                uint rgba = r[s];
                uint cr = rgba & 0xFF;
                uint cg = (rgba >> 8) & 0xFF;
                uint cb = (rgba >> 16) & 0xFF;
                uint ca = (rgba >> 24) & 0xFF;
                v[d] = vec4(float(cr) / 255.0, float(cg) / 255.0, float(cb) / 255.0, float(ca) / 255.0);
                break;
            }
            case 29: {
                // gsub
                submit_addr = r[d];
                submit_len = r[s];
                break;
            }
        }

        // gsub suspends vcop execution
        if (op == 29) {
            break;
        }
    }

    // copy data into output vertex buffer
    // note: DB vertex format is 32 bytes, or 8 words long
    if (submit_len > 0) {
        uint word_len = submit_len * 8;
        for (uint i = 0; i < word_len; i++) {
            vtxdata[i] = workmem[submit_addr + i];
        }

        // write draw call info
        drawcmd.num_vertices = submit_len;
        drawcmd.num_instances = 1;
        drawcmd.first_vertex = 0;
        drawcmd.first_instance = 0;
    }
    else {
        // write dummy draw call
        drawcmd.num_vertices = 0;
        drawcmd.num_instances = 0;
        drawcmd.first_vertex = 0;
        drawcmd.first_instance = 0;
    }
}