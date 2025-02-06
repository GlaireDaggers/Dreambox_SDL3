namespace DreamboxVM.Graphics;

using System.Runtime.CompilerServices;
using SDL3;

/// <summary>
/// Wrapper around an SDL3 index buffer
/// </summary>
/// <typeparam name="TIndex">The type of index contained in this buffer</typeparam>
class IndexBuffer<TIndex> : GraphicsBuffer
    where TIndex : unmanaged
{
    public readonly int indexCount;

    public IndexBuffer(GraphicsDevice device, int numIndices)
        : base(device, Unsafe.SizeOf<TIndex>() * numIndices, SDL.SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_INDEX)
    {
        indexCount = numIndices;
    }

    public override void Dispose()
    {
        SDL.SDL_ReleaseGPUBuffer(device.handle, handle);
    }
}