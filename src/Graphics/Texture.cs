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

    private static int CalculateDDSLevelSize(
        int width,
        int height,
        SDL.SDL_GPUTextureFormat format
    ) {
        if (format == SDL.SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_R8G8B8A8_UNORM || format == SDL.SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_B8G8R8A8_UNORM)
        {
            return (((width * 32) + 7) / 8) * height;
        }
        else if (format == SDL.SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_R16G16B16A16_FLOAT)
        {
            return (((width * 64) + 7) / 8) * height;
        }
        else if (format == SDL.SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_R32G32B32A32_FLOAT)
        {
            return (((width * 128) + 7) / 8) * height;
        }
        else
        {
            int blockSize = 16;
            if (format == SDL.SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_BC1_RGBA_UNORM)
            {
                blockSize = 8;
            }
            width = Math.Max(width, 1);
            height = Math.Max(height, 1);
            return (
                ((width + 3) / 4) *
                ((height + 3) / 4) *
                blockSize
            );
        }
    }

    public static Texture ParseDDS(GraphicsDevice graphicsDevice, BinaryReader reader, nint copyPass, nint cmdBuf)
    {
        // DDS loading extension, based on MojoDDS
        // Taken from https://github.com/FNA-XNA/FNA/blob/master/src/Graphics/Texture.cs

        // A whole bunch of magic numbers, yay DDS!
        const uint DDS_MAGIC = 0x20534444;
        const uint DDS_HEADERSIZE = 124;
        const uint DDS_PIXFMTSIZE = 32;
        const uint DDSD_HEIGHT = 0x2;
        const uint DDSD_WIDTH = 0x4;
        const uint DDSD_PITCH = 0x8;
        const uint DDSD_LINEARSIZE = 0x80000;
        const uint DDSD_REQ = (
            /* Per the spec, this should also be or'd with DDSD_CAPS | DDSD_FMT,
                * but some compression tools don't obey the spec, so here we are...
                */
            DDSD_HEIGHT | DDSD_WIDTH
        );
        const uint DDSCAPS_MIPMAP = 0x400000;
        const uint DDSCAPS_TEXTURE = 0x1000;
        const uint DDSCAPS2_CUBEMAP = 0x200;
        const uint DDPF_FOURCC = 0x4;
        const uint DDPF_RGB = 0x40;
        const uint FOURCC_DXT1 = 0x31545844;
        const uint FOURCC_DXT3 = 0x33545844;
        const uint FOURCC_DXT5 = 0x35545844;
        const uint FOURCC_DX10 = 0x30315844;
        const uint pitchAndLinear = (
            DDSD_PITCH | DDSD_LINEARSIZE
        );

        // File should start with 'DDS '
        if (reader.ReadUInt32() != DDS_MAGIC)
        {
            throw new NotSupportedException("Not a DDS!");
        }

        // Texture info
        uint size = reader.ReadUInt32();
        if (size != DDS_HEADERSIZE)
        {
            throw new NotSupportedException("Invalid DDS header!");
        }
        uint flags = reader.ReadUInt32();
        if ((flags & DDSD_REQ) != DDSD_REQ)
        {
            throw new NotSupportedException("Invalid DDS flags!");
        }
        if ((flags & pitchAndLinear) == pitchAndLinear)
        {
            throw new NotSupportedException("Invalid DDS flags!");
        }

        int height = reader.ReadInt32();
        int width = reader.ReadInt32();
        reader.ReadUInt32(); // dwPitchOrLinearSize, unused
        reader.ReadUInt32(); // dwDepth, unused
        int levels = reader.ReadInt32();

        // "Reserved"
        reader.ReadBytes(4 * 11);

        // Format info
        uint formatSize = reader.ReadUInt32();
        if (formatSize != DDS_PIXFMTSIZE)
        {
            throw new NotSupportedException("Bogus PIXFMTSIZE!");
        }
        uint formatFlags = reader.ReadUInt32();
        uint formatFourCC = reader.ReadUInt32();
        uint formatRGBBitCount = reader.ReadUInt32();
        uint formatRBitMask = reader.ReadUInt32();
        uint formatGBitMask = reader.ReadUInt32();
        uint formatBBitMask = reader.ReadUInt32();
        uint formatABitMask = reader.ReadUInt32();

        // dwCaps "stuff"
        uint caps = reader.ReadUInt32();
        if ((caps & DDSCAPS_TEXTURE) == 0)
        {
            throw new NotSupportedException("Not a texture!");
        }

        bool isCube = false;

        uint caps2 = reader.ReadUInt32();
        if (caps2 != 0)
        {
            if ((caps2 & DDSCAPS2_CUBEMAP) == DDSCAPS2_CUBEMAP)
            {
                isCube = true;
            }
            else
            {
                throw new NotSupportedException("Invalid caps2!");
            }
        }

        reader.ReadUInt32(); // dwCaps3, unused
        reader.ReadUInt32(); // dwCaps4, unused

        // "Reserved"
        reader.ReadUInt32();

        // Mipmap sanity check
        if ((caps & DDSCAPS_MIPMAP) != DDSCAPS_MIPMAP)
        {
            levels = 1;
        }

        SDL.SDL_GPUTextureFormat format;

        // Determine texture format
        if ((formatFlags & DDPF_FOURCC) == DDPF_FOURCC)
        {
            switch (formatFourCC)
            {
                case 0x71: // D3DFMT_A16B16G16R16F
                    format = SDL.SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_R16G16B16A16_FLOAT;
                    break;
                case 0x74: // D3DFMT_A32B32G32R32F
                    format = SDL.SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_R32G32B32A32_FLOAT;
                    break;
                case FOURCC_DXT1:
                    format = SDL.SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_BC1_RGBA_UNORM;
                    break;
                case FOURCC_DXT3:
                    format = SDL.SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_BC2_RGBA_UNORM;
                    break;
                case FOURCC_DXT5:
                    format = SDL.SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_BC3_RGBA_UNORM;
                    break;
                case FOURCC_DX10:
                    // If the fourCC is DX10, there is an extra header with additional format information.
                    uint dxgiFormat = reader.ReadUInt32();

                    // These values are taken from the DXGI_FORMAT enum.
                    switch (dxgiFormat)
                    {
                        case 2:
                            format = SDL.SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_R32G32B32A32_FLOAT;
                            break;

                        case 10:
                            format = SDL.SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_R16G16B16A16_FLOAT;
                            break;

                        case 71:
                            format = SDL.SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_BC1_RGBA_UNORM;
                            break;

                        case 74:
                            format = SDL.SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_BC2_RGBA_UNORM;
                            break;

                        case 77:
                            format = SDL.SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_BC3_RGBA_UNORM;
                            break;

                        default:
                            throw new NotSupportedException(
                                "Unsupported DDS texture format"
                            );
                    }

                    uint resourceDimension = reader.ReadUInt32();

                    // These values are taken from the D3D10_RESOURCE_DIMENSION enum.
                    switch (resourceDimension)
                    {
                        case 0: // Unknown
                        case 1: // Buffer
                            throw new NotSupportedException(
                                "Unsupported DDS texture format"
                            );
                        default:
                            break;
                    }

                    /*
                        * This flag seemingly only indicates if the texture is a cube map.
                        * This is already determined above. Cool!
                        */
                    reader.ReadUInt32();

                    /*
                        * Indicates the number of elements in the texture array.
                        * We don't support texture arrays so just throw if it's greater than 1.
                        */
                    uint arraySize = reader.ReadUInt32();

                    if (arraySize > 1)
                    {
                        throw new NotSupportedException(
                            "Unsupported DDS texture format"
                        );
                    }

                    reader.ReadUInt32(); // reserved

                    break;
                default:
                    throw new NotSupportedException(
                        "Unsupported DDS texture format"
                    );
            }
        }
        else if ((formatFlags & DDPF_RGB) == DDPF_RGB)
        {
            if (formatRGBBitCount != 32)
                throw new NotSupportedException("Unsupported DDS texture format: Alpha channel required");

            bool isBgra = (formatRBitMask == 0x00FF0000 &&
                formatGBitMask == 0x0000FF00 &&
                formatBBitMask == 0x000000FF &&
                formatABitMask == 0xFF000000);
            bool isRgba = (formatRBitMask == 0x000000FF &&
                formatGBitMask == 0x0000FF00 &&
                formatBBitMask == 0x00FF0000 &&
                formatABitMask == 0xFF000000);

            if (isBgra)
                format = SDL.SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_B8G8R8A8_UNORM;
            else if (isRgba)
                format = SDL.SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_R8G8B8A8_UNORM;
            else
                throw new NotSupportedException("Unsupported DDS texture format: Only RGBA and BGRA are supported");
        }
        else
        {
            throw new NotSupportedException(
                "Unsupported DDS texture format"
            );
        }

        if (isCube)
        {
            throw new NotImplementedException();
        }

        var tex = new Texture(graphicsDevice, format, width, height, levels, SDL.SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER);

        for (int i = 0; i < levels; i++) {
            int w = width >> i;
            int h = height >> i;
            if (w < 1) w = 1;
            if (h < 1) h = 1;

            int mipSize = CalculateDDSLevelSize(w, h, format);
            byte[] data = reader.ReadBytes(mipSize);

            tex.SetData(copyPass, cmdBuf, data.AsSpan(), i, 0, 0, 0, 0, w, h, 1, false);
        }

        return tex;
    }

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

    /// <summary>
    /// Begin downloading a portion of this buffer from the GPU.
    /// </summary>
    /// <param name="copyPass">A copy pass that the download will happen within</param>
    public void GetData(nint copyPass, int mipLevel, int layer, int x, int y, int z, int w, int h, int d)
    {
        SDL.SDL_DownloadFromGPUTexture(copyPass, new SDL.SDL_GPUTextureRegion() {
            texture = handle,
            mip_level = (uint)mipLevel,
            layer = (uint)layer,
            x = (uint)x,
            y = (uint)y,
            z = (uint)z,
            w = (uint)w,
            h = (uint)h,
            d = (uint)d,
        }, new SDL.SDL_GPUTextureTransferInfo() {
            transfer_buffer = _transferBuffer,
            offset = 0,
            pixels_per_row = 0,
            rows_per_layer = 0
        });
    }

    /// <summary>
    /// Read data previously downloaded from the GPU via GetData. Note that GetData is not guaranteed to complete until the command buffer it was recorded to has signalled its fence.
    /// </summary>
    /// <param name="outData">Output span to read data into</param>
    public void ReadData<TData>(Span<TData> outData)
        where TData : unmanaged
    {
        unsafe
        {
            var transferPtr = (void*)SDL.SDL_MapGPUTransferBuffer(device.handle, _transferBuffer, true);
            fixed (void* dst = outData)
            {
                Unsafe.CopyBlock(dst, transferPtr, (uint)(Unsafe.SizeOf<TData>() * outData.Length));
            }
            SDL.SDL_UnmapGPUTransferBuffer(device.handle, _transferBuffer);
        }
    }

    public override void Dispose()
    {
        SDL.SDL_ReleaseGPUTransferBuffer(device.handle, _transferBuffer);
        SDL.SDL_ReleaseGPUTexture(device.handle, handle);
    }
}