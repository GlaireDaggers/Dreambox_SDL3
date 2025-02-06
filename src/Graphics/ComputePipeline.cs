namespace DreamboxVM.Graphics;

using System.Text;
using SDL3;

/// <summary>
/// Wrapper around the SDL3 compute pipeline
/// </summary>
class ComputePipeline : GraphicsResource
{
    private static unsafe nint CreatePipeline(GraphicsDevice device, Span<byte> code, string entryPoint, int numSamplers, int numReadonlyStorageTextures, int numReadonlyStorageBuffers, int numReadWriteStorageTextures,
        int numReadWriteStorageBuffers, int numUniformBuffers, int threadcount_x, int threadcount_y, int threadcount_z)
    {
        var ep = Encoding.UTF8.GetBytes(entryPoint + '\0');

        fixed (byte* codePtr = code)
        fixed (byte* ePtr = ep)
        {
            var pipelineInfo = new SDL.SDL_GPUComputePipelineCreateInfo()
            {
                code_size = (nuint)code.Length,
                code = codePtr,
                entrypoint = ePtr,
                format = SDL.SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_SPIRV,
                num_samplers = (uint)numSamplers,
                num_readonly_storage_textures = (uint)numReadonlyStorageTextures,
                num_readonly_storage_buffers = (uint)numReadonlyStorageBuffers,
                num_readwrite_storage_textures = (uint)numReadWriteStorageTextures,
                num_readwrite_storage_buffers = (uint)numReadWriteStorageBuffers,
                num_uniform_buffers = (uint)numUniformBuffers,
                threadcount_x = (uint)threadcount_x,
                threadcount_y = (uint)threadcount_y,
                threadcount_z = (uint)threadcount_z,
            };

            return SDL.SDL_CreateGPUComputePipeline(device.handle, pipelineInfo);
        }
    }

    public ComputePipeline(GraphicsDevice device, Span<byte> code, string entryPoint, int numSamplers, int numReadonlyStorageTextures, int numReadonlyStorageBuffers, int numReadWriteStorageTextures,
        int numReadWriteStorageBuffers, int numUniformBuffers, int threadcount_x, int threadcount_y, int threadcount_z)
        : base(device, CreatePipeline(device, code, entryPoint, numSamplers, numReadonlyStorageTextures, numReadonlyStorageBuffers, numReadWriteStorageTextures, numReadWriteStorageBuffers, numUniformBuffers,
            threadcount_x, threadcount_y, threadcount_z))
    {
    }

    public override void Dispose()
    {
        SDL.SDL_ReleaseGPUComputePipeline(device.handle, handle);
    }
}