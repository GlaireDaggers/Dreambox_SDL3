# Dreambox SDL3
A (mostly complete) SDL3 port of the Dreambox fantasy console runtime

# What works, what doesn't?
*Mostly* everything works. There's just a couple of things that have not been implemented:

- YUV textures are still on the to-do list. For now, attempting to allocate them will crash.
- Depth queries are not implemented. This was built on occlusion queries, which are just not plain not supported on SDL3. For now, attempting to submit a depth query or retrieve its results will crash. If there's enough demand for it, I may eventually consider re-implementing these via compute shader or something.
- The built-in "boot UI" has not yet been implemented.

# Changes
This port saves user data into a new folder compared to the old FNA version.

- On Windows, it will save to `%userprofile%\AppData\Roaming\Dreambox\`
- On Linux, it will save to `~/.local/share/Dreambox/`

The config JSON file format is not compatible with the old FNA version. However, the memory card save file format is identical.

# New features!
New extensions have been added to the Dreambox API in this port which didn't exist in the original (NOTE: these are currently considered experimental and subject to change). These are:

- `vdp_setVUCData` allows you to set one of 16 "constant data" slots of the "Vertex Unit" (VU) with a vector value.
- `vdp_setVULayout` allows you to configure one of 8 "vertex input slots" of the VU, representing vertex input data (such as position, normal, texcoord, color, etc)
- `vdp_setVUStride` allows you to configure the byte stride of input vertex data to the VU
- `vdp_uploadVUProgram` allows you to upload a VU program of up to 64 instructions, which will execute per-vertex and is responsible for transforming input vertex data into the existing built-in output fields (position, texcoord, color, and ocolor)
- `vdp_submitVU` allows you to submit vertex input data to the VU (note: the VU will also execute for vertices submitted via `vdp_drawGeometry` and `vdp_drawGeometryPacked`, but `vdp_submitVU` makes no assumptions about the size or format of the input data)
- `vdp_setSampleParamsSlot` allows you to configure the texture sample parameters for each of two texture units TU0 and TU1 (see below)
- `vdp_bindTextureSlot` allows you to bind a texture to either texture unit
- `vdp_setTexCombine` allows you to configure how each texture unit is combined, as well as how the result is then combined with vertex color
- `vdp_allocRenderTexture` allows you to allocate an RGBA32 texture which can be rendered into (along with an associated depth buffer).
- `vdp_setRenderTarget` allows you to set a previously allocated render texture to be rendered into, or alternatively to set the current target to the built-in framebuffer.

## Dual texturing
This port of Dreambox adds dual texturing support, partially inspired by the 3DFX Voodoo line. There are two texture units, TU0 and TU1, which can each have a texture & sample parameters set.
The new pixel pipeline can be diagrammed like so:

```
┌──────┐    ┌──────┐
│ TU0  │    │ TU1  │
└──────┘    └──────┘
   ║  ┌──────┐  ║
   ╚═>A (OP) B<═╝
      └──────┘
┌──────┐  ║
│ COL  │  ╚═════╗
└──────┘        ║
   ║  ┌──────┐  ║  ┌──────┐
   ╚═>A (OP) B<═╝  │ OCOL │
      └──────┘     └──────┘
          ║  ┌─────┐  ║
          ╚═>A  +  B<═╝
             └─────┘
                ║
                ╨
              (out)
```

First, the output of both TMUs is combined with a configurable operation. Then, this result is combined with the vertex color using another configurable operation. Finally, the result is summed with the vertex offset color & forms the final color which is blended with the framebuffer.
The operations which can be used to combine two colors A and B are as follows:

| Operation     | Formula                   |
| ------------- | ------------------------- |
| NONE(A, B)    | A                         |
| MUL(A, B)     | A * B                     |
| ADD(A, B)     | A + B                     |
| SUB(A, B)     | A - B                     |
| MIX(A, B)     | lerp(A, B, B.a)           |
| DOT3(A, B)    | dot(A * 2 - 1, B * 2 - 1) |

## Dual texture UVs
Both TUs can address textures using independant UV coordinates. From the VU's point of view, it still only outputs four slots - pos, tex, col, and ocol, but tex now packs both coordinate sets in a single vec4 (xy is sent to TU0, and zw is sent to TU1). Since the PackedVertex format only includes a single UV set, the default "compatibility" VU program which is loaded by default just provides the same UVs to both texture units.
