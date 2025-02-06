namespace DreamboxVM.Graphics;

using System.Runtime.CompilerServices;
using SDL3;

class Texture : GraphicsResource
{
    public readonly SDL.SDL_GPUTextureType type;
    public readonly SDL.SDL_GPUTextureFormat format;
    public readonly SDL.SDL_GPUTextureUsageFlags usage;
    public readonly int width;
    public readonly int height;
    public readonly int levels;

    private readonly nint _transferBuffer;

    private static nint CreateTexture(GraphicsDevice device, SDL.SDL_GPUTextureFormat format, int width, int height, int levels, SDL.SDL_GPUTextureUsageFlags usage)
    {
        return SDL.SDL_CreateGPUTexture(device.handle, new SDL.SDL_GPUTextureCreateInfo() {
            type = SDL.SDL_GPUTextureType.SDL_GPU_TEXTURETYPE_2D,
            format = format,
            usage = usage,
            width = (uint)width,
            height = (uint)height,
            layer_count_or_depth = 1,
            num_levels = (uint)levels,
            sample_count = SDL.SDL_GPUSampleCount.SDL_GPU_SAMPLECOUNT_1
        });
    }

    public Texture(GraphicsDevice device, SDL.SDL_GPUTextureFormat format, int width, int height, int levels, SDL.SDL_GPUTextureUsageFlags usage)
        : base(device, CreateTexture(device, format, width, height, levels, usage))
    {
        type = SDL.SDL_GPUTextureType.SDL_GPU_TEXTURETYPE_2D;
        this.format = format;
        this.usage = usage;
        this.width = width;
        this.height = height;
        this.levels = levels;

        _transferBuffer = SDL.SDL_CreateGPUTransferBuffer(device.handle, new SDL.SDL_GPUTransferBufferCreateInfo()
        {
            usage = SDL.SDL_GPUTransferBufferUsage.SDL_GPU_TRANSFERBUFFERUSAGE_UPLOAD,
            size = SDL.SDL_CalculateGPUTextureFormatSize(format, (uint)width, (uint)height, 1)
        });
    }

    /// <summary>
    /// Upload data to the texture
    /// </summary>
    /// <param name="pixels">Span of pixel data</param>
    /// <param name="cycleTexture">Whether to cycle the underlying texture (see https://moonside.games/posts/sdl-gpu-concepts-cycling/)</param>
    public void SetData<TData>(nint copyPass, nint cmdBuf, Span<TData> data, int mipLevel, int layer, int x, int y, int z, int w, int h, int d, bool cycleTexture)
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
        SDL.SDL_UploadToGPUTexture(copyPass, new () {
            transfer_buffer = _transferBuffer,
            offset = 0,
            pixels_per_row = 0,
            rows_per_layer = 0
        }, new () {
            texture = handle,
            mip_level = (uint)mipLevel,
            layer = (uint)layer,
            x = (uint)x,
            y = (uint)y,
            z = (uint)z,
            w = (uint)w,
            h = (uint)h,
            d = (uint)d,
        }, cycleTexture);
    }

    public override void Dispose()
    {
        SDL.SDL_ReleaseGPUTransferBuffer(device.handle, _transferBuffer);
        SDL.SDL_ReleaseGPUTexture(device.handle, handle);
    }
}