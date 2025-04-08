using System.Numerics;
using SDL3;

namespace DreamboxVM.Graphics;

struct BlitVertex : IVertex
{
    public Vector2 position;
    public Vector2 texcoord;

    public static SDL.SDL_GPUVertexAttribute[] GetLayout()
    {
        return [
            new SDL.SDL_GPUVertexAttribute()
            {
                location = 0,
                buffer_slot = 0,
                format = SDL.SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT2,
                offset = 0
            },
            new SDL.SDL_GPUVertexAttribute()
            {
                location = 1,
                buffer_slot = 0,
                format = SDL.SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT2,
                offset = 8
            },
        ];
    }

    public static void SetBlitQuad(GraphicsDevice graphicsDevice, VertexBuffer<BlitVertex> vtxBuffer, float widthScale, float heightScale)
    {
        nint cmdBuf = SDL.SDL_AcquireGPUCommandBuffer(graphicsDevice.handle);
        nint copyPass = SDL.SDL_BeginGPUCopyPass(cmdBuf);

        vtxBuffer.SetData<BlitVertex>(copyPass, [
            new () {
                position = new Vector2(-widthScale, -heightScale),
                texcoord = new Vector2(0.0f, 1.0f)
            },
            new () {
                position = new Vector2(widthScale, -heightScale),
                texcoord = new Vector2(1.0f, 1.0f)
            },
            new () {
                position = new Vector2(-widthScale, heightScale),
                texcoord = new Vector2(0.0f, 0.0f)
            },

            new () {
                position = new Vector2(widthScale, -heightScale),
                texcoord = new Vector2(1.0f, 1.0f)
            },
            new () {
                position = new Vector2(widthScale, heightScale),
                texcoord = new Vector2(1.0f, 0.0f)
            },
            new () {
                position = new Vector2(-widthScale, heightScale),
                texcoord = new Vector2(0.0f, 0.0f)
            },
        ], 0, true);

        SDL.SDL_EndGPUCopyPass(copyPass);
        SDL.SDL_SubmitGPUCommandBuffer(cmdBuf);
    }
}