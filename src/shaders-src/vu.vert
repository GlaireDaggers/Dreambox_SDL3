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

        uint op = instr & 0x3F;
        uint dst = (instr >> 6) & 0xF;
        uint src = (instr >> 10) & 0xF;
        
        uint sx = (instr >> 14) & 3;
        uint sy = (instr >> 16) & 3;
        uint sz = (instr >> 18) & 3;
        uint sw = (instr >> 20) & 3;

        bool mx = ((instr >> 22) & 1) == 1;
        bool my = ((instr >> 23) & 1) == 1;
        bool mz = ((instr >> 24) & 1) == 1;
        bool mw = ((instr >> 25) & 1) == 1;

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
                // exp
                reg[dst] = exp(reg[src]);
                break;
            }
            case 13: {
                // log
                reg[dst] = log(reg[src]);
                break;
            }
            case 14: {
                // min
                reg[dst] = min(reg[dst], reg[src]);
                break;
            }
            case 15: {
                // max
                reg[dst] = max(reg[dst], reg[src]);
                break;
            }
            case 16: {
                // sin
                reg[dst] = sin(reg[src]);
                break;
            }
            case 17: {
                // cos
                reg[dst] = cos(reg[src]);
                break;
            }
            case 18: {
                // tan
                reg[dst] = tan(reg[src]);
                break;
            }
            case 19: {
                // asin
                reg[dst] = asin(reg[src]);
                break;
            }
            case 20: {
                // acos
                reg[dst] = acos(reg[src]);
                break;
            }
            case 21: {
                // atan
                reg[dst] = atan(reg[src]);
                break;
            }
            case 22: {
                // atan2
                reg[dst] = atan(reg[dst], reg[src]);
                break;
            }
            case 23: {
                // shf
                vec4 v = reg[src];
                reg[dst] = mix(reg[dst], vec4(v[sx], v[sy], v[sz], v[sw]), bvec4(mx, my, mz, mw));
                break;
            }
            case 24: {
                // mulm
                vec4 c0 = reg[src];
                vec4 c1 = reg[src + 1];
                vec4 c2 = reg[src + 2];
                vec4 c3 = reg[src + 3];
                reg[dst] = mat4(c0, c1, c2, c3) * reg[dst];
                break;
            }
        }

        if (op == 0x3F) {
            // end
            break;
        }
    }

    gl_Position = odata[0];
    fTex0 = odata[1].xy;
    fTex1 = odata[1].zw;
    fCol = clamp(odata[2], 0.0, 1.0);
    fOCol = clamp(odata[3], 0.0, 1.0);
}