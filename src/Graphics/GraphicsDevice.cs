namespace DreamboxVM.Graphics;

using SDL3;

/// <summary>
/// Wrapper around the SDL3 graphics device
/// </summary>
class GraphicsDevice : IDisposable
{
    public readonly nint handle;
    public readonly SDL.SDL_GPUTextureFormat swapchainFormat;

    private nint _window;

    public GraphicsDevice(nint window)
    {
        handle = SDL.SDL_CreateGPUDevice(SDL.SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_SPIRV, false, null);
        _window = window;

        if (handle == 0)
        {
            throw new Exception("Failed to create GPU device: " + SDL.SDL_GetError());
        }

        if (!SDL.SDL_ClaimWindowForGPUDevice(handle, window))
        {
            throw new Exception("Failed to claim window: " + SDL.SDL_GetError());
        }

        // init swapchain
        if (!SDL.SDL_SetGPUSwapchainParameters(handle, _window, SDL.SDL_GPUSwapchainComposition.SDL_GPU_SWAPCHAINCOMPOSITION_SDR,
            SDL.SDL_GPUPresentMode.SDL_GPU_PRESENTMODE_VSYNC))
        {
            throw new Exception("Failed to set swapchain params: " + SDL.SDL_GetError());
        }

        swapchainFormat = SDL.SDL_GetGPUSwapchainTextureFormat(handle, _window);
    }

    public void WaitForIdle()
    {
        SDL.SDL_WaitForGPUIdle(handle);
    }

    public void Dispose()
    {
        SDL.SDL_DestroyGPUDevice(handle);
    }
}