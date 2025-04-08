namespace DreamboxVM.Graphics;

using System.Text;
using SDL3;

class Shader : GraphicsResource
{
    public static Shader LoadShader(GraphicsDevice device, string path, SDL.SDL_GPUShaderStage stage,
        int numSamplers, int numStorageBuffers, int numUniformBuffers)
    {
        Console.WriteLine("Loading shader: " + path);
        byte[] data = File.ReadAllBytes(path);
        return new Shader(device, data, "main", stage, numSamplers, numStorageBuffers, numUniformBuffers);
    }

    private static unsafe nint CreateShader(nint gpuDevice, Span<byte> bytes, string entryPoint, SDL.SDL_GPUShaderStage stage,
        int numSamplers, int numStorageBuffers, int numUniformBuffers)
    {
        var ep = Encoding.UTF8.GetBytes(entryPoint + '\0');

        fixed (byte* bPtr = bytes)
        fixed (byte* ePtr = ep)
        {
            var createInfo = new SDL.SDL_GPUShaderCreateInfo() {
                code = bPtr,
                code_size = (uint)bytes.Length,
                entrypoint = ePtr,
                format = SDL.SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_SPIRV,
                stage = stage,
                num_samplers = (uint)numSamplers,
                num_storage_buffers = (uint)numStorageBuffers,
                num_uniform_buffers = (uint)numUniformBuffers
            };

            nint val = SDL.SDL_CreateGPUShader(gpuDevice, createInfo);

            if (val == 0)
            {
                throw new Exception("Failed loading shader: " + SDL.SDL_GetError());
            }

            return val;
        }
    }

    public Shader(GraphicsDevice device, Span<byte> shaderData, string entryPoint, SDL.SDL_GPUShaderStage stage,
        int numSamplers, int numStorageBuffers, int numUniformBuffers)
        : base(device, CreateShader(device.handle, shaderData, entryPoint, stage, numSamplers, numStorageBuffers, numUniformBuffers))
    {
    }

    public override void Dispose()
    {
        SDL.SDL_ReleaseGPUShader(device.handle, handle);
    }
}