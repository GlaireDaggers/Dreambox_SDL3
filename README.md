# Dreambox SDL3
A (mostly complete) SDL3 port of the Dreambox fantasy console runtime

# What works, what doesn't?
*Mostly* everything works. There's just a couple of things that have not been implemented:

- YUV textures are still on the to-do list. For now, attempting to allocate them will crash.
- Depth queries are not implemented. This was built on occlusion queries, which are just not plain not supported on SDL3. For now, attempting to submit a depth query or retrieve its results will crash. If there's enough demand for it, I may eventually consider re-implementing these via compute shader or something.
- The built-in "boot UI" has not yet been implemented.

# Changes
This port saves user data into a new folder compared to the old FNA version.

- On Windows, it will save to `{USERS}\AppData\Roaming\Dreambox\`
- On Linux, it will save to `~/.local/share/Dreambox/`

The config JSON file format is not compatible with the old FNA version. However, the memory card save file format is identical.

# New features!
New extensions have been added to the Dreambox API in this port which didn't exist in the original. These are:

- vdp_loadTransform allows you to load a 4x4 matrix to transform vertex positions instead of having to manually transform vertex positions prior to submission.
- vdp_setLighting allows you to enable or disable built-in lighting. If enabled, the "color" field is instead treated as encoding vertex normals (the 0..1 range is expanded to the -1..1 range).
- vdp_loadLightTransforms allows you to load a pair of 4x4 matrices which are used to compute lighting from input normals, if lighting is enabled. This works a bit like the lighting feature of the Playstation 1 - one matrix is used to first transform input normals into one intensity per light source (3 channels = 3 directional lights), and then another matrix is used to multiply those 3 intensities against 3 light colors and sum into final vertex color (as well as adding ambient light color). You could alternatively use just the first matrix for SH lighting & set the second matrix to identity.
- vdp_allocRenderTexture allows you to allocate an RGBA32 texture which can be rendered into (along with an associated depth buffer).
- vdp_setRenderTarget allows you to set a previously allocated render texture to be rendered into, or alternatively to set the current target to the built-in framebuffer.
