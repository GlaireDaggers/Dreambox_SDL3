#version 450

layout(location = 0) in vec4 vtx[8];

layout(location = 0) out vec2 fTex0;
layout(location = 1) out vec2 fTex1;
layout(location = 2) out vec4 fCol;
layout(location = 3) out vec4 fOCol;

layout(std430, set = 0, binding = 0) buffer ProgramData {
   uint data[];
} programData;

layout(set = 1, binding = 0) uniform UBO {
    vec4 cdata[16];
} ubo;

void main() {
    vec4 odata[4];
    vec4 reg[16];

    for (int i = 0; i < 64; i++) {
        uint instr = programData.data[i];

        uint op = instr & 0xF;
        uint dst = (instr >> 4) & 0xF;
        uint src = (instr >> 8) & 0xF;
        
        uint sx = (instr >> 12) & 3;
        uint sy = (instr >> 14) & 3;
        uint sz = (instr >> 16) & 3;
        uint sw = (instr >> 18) & 3;

        bool mx = ((instr >> 20) & 1) == 1;
        bool my = ((instr >> 21) & 1) == 1;
        bool mz = ((instr >> 22) & 1) == 1;
        bool mw = ((instr >> 23) & 1) == 1;

        switch (op) {
            case 0: {
                // ld
                reg[dst] = vtx[src & 7];
                break;
            }
            case 1: {
                // st
                odata[dst & 3] = reg[src];
                break;
            }
            case 2: {
                // ldc
                reg[dst] = ubo.cdata[src];
                break;
            }
            case 3: {
                // add
                reg[dst] += reg[src];
                break;
            }
            case 4: {
                // sub
                reg[dst] -= reg[src];
                break;
            }
            case 5: {
                // mul
                reg[dst] *= reg[src];
                break;
            }
            case 6: {
                // div
                reg[dst] /= reg[src];
                break;
            }
            case 7: {
                // dot
                reg[dst] = vec4(dot(reg[dst], reg[src]), 0.0, 0.0, 0.0);
                break;
            }
            case 8: {
                // abs
                reg[dst] = abs(reg[src]);
                break;
            }
            case 9: {
                // sign
                reg[dst] = sign(reg[src]);
                break;
            }
            case 10: {
                // sqrt
                reg[dst] = sqrt(reg[src]);
                break;
            }
            case 11: {
                // pow
                reg[dst] = pow(reg[dst], reg[src]);
                break;
            }
            case 12: {
                // min
                reg[dst] = min(reg[dst], reg[src]);
                break;
            }
            case 13: {
                // max
                reg[dst] = max(reg[dst], reg[src]);
                break;
            }
            case 14: {
                // shf
                vec4 v = reg[src];
                reg[dst] = mix(reg[dst], vec4(v[sx], v[sy], v[sz], v[sw]), bvec4(mx, my, mz, mw));
                break;
            }
            case 15: {
                // end
                break;
            }
        }

        if (op == 15) {
            break;
        }
    }

    gl_Position = odata[0];
    fTex0 = odata[1].xy;
    fTex1 = odata[1].zw;
    fCol = clamp(odata[2], 0.0, 1.0);
    fOCol = clamp(odata[3], 0.0, 1.0);
}