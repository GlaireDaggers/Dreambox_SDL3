namespace DreamboxVM.Windows;

using System.Numerics;
using ImGuiNET;
using SDL3;

class WindowBase(string title, Vector2 windowSize)
{
    public string title = title;
    public Vector2 windowSize = windowSize;

    public void DrawGui(ref bool open)
    {
        ImGui.SetNextWindowSize(windowSize, ImGuiCond.Once);
        if (ImGui.Begin(title, ref open))
        {
            DrawContents();
            ImGui.End();
        }
    }

    public virtual void OnClose()
    {
    }

    public virtual void HandleEvent(in SDL.SDL_Event e)
    {
    }

    protected virtual void DrawContents()
    {
    }
}