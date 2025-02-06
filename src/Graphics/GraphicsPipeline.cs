namespace DreamboxVM.Graphics;

using SDL3;

/// <summary>
/// Wrapper around the SDL3 graphics pipeline
/// </summary>
class GraphicsPipeline : GraphicsResource
{
    private static unsafe nint CreatePipeline(GraphicsDevice device, Span<SDL.SDL_GPUColorTargetDescription> colorTargets, SDL.SDL_GPUTextureFormat depthFormat, bool hasDepthTarget,
        Shader vertexShader, Shader fragmentShader,
        in SDL.SDL_GPUVertexBufferDescription vtxDesc, Span<SDL.SDL_GPUVertexAttribute> vtxLayout,
        SDL.SDL_GPUPrimitiveType primitiveType,
        in SDL.SDL_GPURasterizerState rasterizerState, in SDL.SDL_GPUMultisampleState multisampleState, in SDL.SDL_GPUDepthStencilState depthStencilState)
    {
        fixed (SDL.SDL_GPUVertexAttribute* attrPtr = vtxLayout)
        fixed (SDL.SDL_GPUVertexBufferDescription* desc = &vtxDesc)
        fixed (SDL.SDL_GPUColorTargetDescription* targetPtr = colorTargets)
        {
            var pipelineInfo = new SDL.SDL_GPUGraphicsPipelineCreateInfo()
            {
                vertex_shader = vertexShader.handle,
                vertex_input_state = new SDL.SDL_GPUVertexInputState() {
                    vertex_buffer_descriptions = desc,
                    num_vertex_buffers = 1,
                    vertex_attributes = attrPtr,
                    num_vertex_attributes = (uint)vtxLayout.Length,
                },
                fragment_shader = fragmentShader.handle,
                primitive_type = primitiveType,
                rasterizer_state = rasterizerState,
                multisample_state = multisampleState,
                depth_stencil_state = depthStencilState,
                target_info = new SDL.SDL_GPUGraphicsPipelineTargetInfo() {
                    color_target_descriptions = targetPtr,
                    num_color_targets = (uint)colorTargets.Length,
                    depth_stencil_format = depthFormat,
                    has_depth_stencil_target = hasDepthTarget
                }
            };

            return SDL.SDL_CreateGPUGraphicsPipeline(device.handle, pipelineInfo);
        }
    }

    /// <summary>
    /// Helper method to create a graphics pipeline for a vertex buffer containing vertices of the given type
    /// </summary>
    public static GraphicsPipeline Create<TVertex>(GraphicsDevice device, Span<SDL.SDL_GPUColorTargetDescription> colorTargets, SDL.SDL_GPUTextureFormat depthFormat, bool hasDepthTarget,
        Shader vertexShader, Shader fragmentShader,
        SDL.SDL_GPUPrimitiveType primitiveType,
        in SDL.SDL_GPURasterizerState rasterizerState, in SDL.SDL_GPUMultisampleState multisampleState, in SDL.SDL_GPUDepthStencilState depthStencilState)
        where TVertex : unmanaged, IVertex
    {
        return new GraphicsPipeline(device, colorTargets, depthFormat, hasDepthTarget, vertexShader, fragmentShader,
            VertexBuffer<TVertex>.GetBufferDescription(), TVertex.GetLayout(),
            primitiveType, rasterizerState, multisampleState, depthStencilState);
    }

    public GraphicsPipeline(GraphicsDevice device, Span<SDL.SDL_GPUColorTargetDescription> colorTargets, SDL.SDL_GPUTextureFormat depthFormat, bool hasDepthTarget,
        Shader vertexShader, Shader fragmentShader,
        in SDL.SDL_GPUVertexBufferDescription vtxDesc, Span<SDL.SDL_GPUVertexAttribute> vtxLayout,
        SDL.SDL_GPUPrimitiveType primitiveType,
        in SDL.SDL_GPURasterizerState rasterizerState, in SDL.SDL_GPUMultisampleState multisampleState, in SDL.SDL_GPUDepthStencilState depthStencilState)
        : base(device, CreatePipeline(device, colorTargets, depthFormat, hasDepthTarget, vertexShader, fragmentShader, vtxDesc, vtxLayout, primitiveType, rasterizerState, multisampleState, depthStencilState))
    {
    }

    public override void Dispose()
    {
        SDL.SDL_ReleaseGPUGraphicsPipeline(device.handle, handle);
    }
}