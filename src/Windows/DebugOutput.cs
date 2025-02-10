using System.Numerics;
using DreamboxVM.ImGuiRendering;
using DreamboxVM.VM;
using ImGuiNET;
using SDL3;

namespace DreamboxVM.Windows;

class DebugOutputWindow : WindowBase
{
    const int MAX_LINES = 2048;

    private static List<string> _messageHistory = [];
    private static bool _autoScroll = true;
    private static bool _queueScroll = false;

    public static void StartLogCapture()
    {
        Runtime.OnLogOutput += HandleLogOutput;
    }

    public DebugOutputWindow() : base("Debug Output", new Vector2(400, 250))
    {
    }

    protected override void DrawContents()
    {
        base.DrawContents();

        ImGui.Checkbox("Auto scroll", ref _autoScroll);

        if (ImGui.BeginChild("##msgscroll", Vector2.Zero, ImGuiChildFlags.None))
        {
            foreach (var line in _messageHistory)
            {
                ImGui.TextWrapped(line);
            }

            if (_queueScroll)
            {
                ImGui.SetScrollHereY();
                _queueScroll = false;
            }

            ImGui.EndChild();
        }
    }

    private static void HandleLogOutput(string msg)
    {
        _messageHistory.Add(msg);

        if (_messageHistory.Count > MAX_LINES) {
            _messageHistory.RemoveAt(0);
        }

        if (_autoScroll) {
            _queueScroll = true;
        }
    }
}