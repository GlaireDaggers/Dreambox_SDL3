using SDL3;

static class PathUtils
{
    public static string GetBasePath()
    {
        return SDL.SDL_GetPrefPath(null, "Dreambox");
    }

    public static string GetPath(string path)
    {
        return Path.Combine(GetBasePath(), path);
    }
}