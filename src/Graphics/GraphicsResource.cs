namespace DreamboxVM.Graphics;

using SDL3;

abstract class GraphicsResource : IDisposable
{
    public readonly GraphicsDevice device;
    public readonly nint handle;

    public GraphicsResource(GraphicsDevice device, nint handle)
    {
        this.device = device;
        this.handle = handle;

        if (handle == 0)
        {
            throw new Exception("Failed creating resource: " + SDL.SDL_GetError());
        }
    }

    public abstract void Dispose();
}