using System.Runtime.CompilerServices;
using SDL3;

namespace DreamboxVM.Graphics;

/// <summary>
/// Wrapper around an SDL3 graphics buffer
/// </summary>
class GraphicsBuffer : GraphicsResource
{
    public readonly int byteLength;
    public readonly SDL.SDL_GPUBufferUsageFlags usage;

    private readonly nint _transferBuffer;

    private static nint CreateBuffer(GraphicsDevice device, int bufferSize, SDL.SDL_GPUBufferUsageFlags usage)
    {
        return SDL.SDL_CreateGPUBuffer(device.handle, new () {
            size = (uint)bufferSize,
            usage = usage,
        });
    }

    public GraphicsBuffer(GraphicsDevice device, int bufferSize, SDL.SDL_GPUBufferUsageFlags usage)
        : base(device, CreateBuffer(device, bufferSize, usage))
    {
        byteLength = bufferSize;
        this.usage = usage;

        _transferBuffer = SDL.SDL_CreateGPUTransferBuffer(device.handle, new SDL.SDL_GPUTransferBufferCreateInfo()
        {
            usage = SDL.SDL_GPUTransferBufferUsage.SDL_GPU_TRANSFERBUFFERUSAGE_UPLOAD,
            size = (uint)bufferSize
        });

        if (_transferBuffer == 0)
        {
            throw new Exception("Failed creating transfer buffer: " + SDL.SDL_GetError());
        }
    }

    private nint _transferPtr;
    private bool _shouldCycle;

    /// <summary>
    /// Get ready to upload data
    /// </summary>
    /// <param name="cycleBuffer">Whether to cycle the underlying buffer (see https://moonside.games/posts/sdl-gpu-concepts-cycling/)</param>
    public void BeginSetData(bool cycleBuffer)
    {
        _transferPtr = SDL.SDL_MapGPUTransferBuffer(device.handle, _transferBuffer, true);
        _shouldCycle = cycleBuffer;
    }

    /// <summary>
    /// Queue a new region of data to be set
    /// </summary>
    /// <param name="data">Span of data to upload</param>
    /// <param name="offset">Byte offset within the buffer</param>
    public void QueueSetData<TData>(nint copyPass, Span<TData> data, int offset)
        where TData : unmanaged
    {
        unsafe
        {
            var transferPtr = (byte*)_transferPtr + offset;
            fixed (void* src = data)
            {
                Unsafe.CopyBlock(transferPtr, src, (uint)(Unsafe.SizeOf<TData>() * data.Length));
            }
        }

        SDL.SDL_UploadToGPUBuffer(copyPass, new SDL.SDL_GPUTransferBufferLocation() { transfer_buffer = _transferBuffer, offset = (uint)offset },
            new SDL.SDL_GPUBufferRegion() { buffer = handle, offset = (uint)offset, size = (uint)(Unsafe.SizeOf<TData>() * data.Length) }, _shouldCycle);

        // only the first SetData should cycle - subsequent calls are assumed to be modifying the same buffer until EndSetData is called
        _shouldCycle = false;
    }

    /// <summary>
    /// Upload queued set data
    /// </summary>
    public void EndSetData()
    {
        SDL.SDL_UnmapGPUTransferBuffer(device.handle, _transferBuffer);
    }

    /// <summary>
    /// Upload data to the buffer
    /// </summary>
    /// <param name="vertices">Span of data to upload</param>
    /// <param name="offset">Byte offset within the buffer</param>
    /// <param name="cycleBuffer">Whether to cycle the underlying buffer (see https://moonside.games/posts/sdl-gpu-concepts-cycling/)</param>
    public void SetData<TData>(nint copyPass, Span<TData> data, int offset, bool cycleBuffer)
        where TData : unmanaged
    {
        // copy data to transfer buffer
        // note: currently for simplicity we stomp on whatever was in the transfer buffer, so right now we just always cycle it
        // if it's uploaded only once this is probably unnecessary, but really not a big deal.

        unsafe
        {
            var transferPtr = (void*)SDL.SDL_MapGPUTransferBuffer(device.handle, _transferBuffer, true);
            fixed (void* src = data)
            {
                Unsafe.CopyBlock(transferPtr, src, (uint)(Unsafe.SizeOf<TData>() * data.Length));
            }
            SDL.SDL_UnmapGPUTransferBuffer(device.handle, _transferBuffer);
        }

        // upload to GPU
        SDL.SDL_UploadToGPUBuffer(copyPass, new SDL.SDL_GPUTransferBufferLocation() { transfer_buffer = _transferBuffer },
            new SDL.SDL_GPUBufferRegion() { buffer = handle, offset = (uint)offset, size = (uint)(Unsafe.SizeOf<TData>() * data.Length) }, cycleBuffer);
    }

    public override void Dispose()
    {
        SDL.SDL_ReleaseGPUTransferBuffer(device.handle, _transferBuffer);
        SDL.SDL_ReleaseGPUBuffer(device.handle, handle);
    }
}