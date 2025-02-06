namespace DreamboxVM.Graphics;

using System.Runtime.CompilerServices;
using SDL3;

/// <summary>
/// Interface for a vertex data struct
/// </summary>
interface IVertex
{
    /// <summary>
    /// Get the vertex buffer layout when using this vertex type
    /// </summary>
    /// <returns>An array of vertex attributes</returns>
    static abstract SDL.SDL_GPUVertexAttribute[] GetLayout();
}

/// <summary>
/// Wrapper around an SDL3 vertex buffer
/// </summary>
/// <typeparam name="TVertex">The type of data contained in this buffer</typeparam>
class VertexBuffer<TVertex> : GraphicsBuffer
    where TVertex : unmanaged, IVertex
{
    public readonly int vertexCount;
    public readonly SDL.SDL_GPUVertexBufferDescription desc;
    public readonly SDL.SDL_GPUVertexAttribute[] attributes;

    public static SDL.SDL_GPUVertexBufferDescription GetBufferDescription()
    {
        return new SDL.SDL_GPUVertexBufferDescription()
        {
            slot = 0,
            pitch = (uint)Unsafe.SizeOf<TVertex>(),
            input_rate = SDL.SDL_GPUVertexInputRate.SDL_GPU_VERTEXINPUTRATE_VERTEX,
            instance_step_rate = 0
        };
    }

    public VertexBuffer(GraphicsDevice device, int numVertices)
        : base(device, Unsafe.SizeOf<TVertex>() * numVertices,
            SDL.SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_VERTEX | SDL.SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_COMPUTE_STORAGE_READ | SDL.SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_COMPUTE_STORAGE_WRITE)
    {
        vertexCount = numVertices;
        desc = GetBufferDescription();

        attributes = TVertex.GetLayout();
    }

    public override void Dispose()
    {
        SDL.SDL_ReleaseGPUBuffer(device.handle, handle);
    }
}