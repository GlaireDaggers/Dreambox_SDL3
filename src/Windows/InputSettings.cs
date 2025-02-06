using System.Numerics;
using DreamboxVM.VM;
using ImGuiNET;
using SDL3;

namespace DreamboxVM.Windows;

class InputSettingsWindow : WindowBase
{
    InputSystem _inputSystem;
    DreamboxConfig _config;

    SDLGamepad? _activeBindGp;
    GamepadButtonBinding? _activeBtnBind;
    GamepadAxisBinding? _activeAxisBind;

    KeyboardButtonBinding? _activeKbBtnBind;
    
    public InputSettingsWindow(DreamboxConfig config, InputSystem inputSystem) : base("Input Settings", new Vector2(400, 250))
    {
        _config = config;
        _inputSystem = inputSystem;
    }

    public override void HandleEvent(in SDL.SDL_Event e)
    {
        base.HandleEvent(e);

        switch ((SDL.SDL_EventType)e.type)
        {
            case SDL.SDL_EventType.SDL_EVENT_GAMEPAD_BUTTON_DOWN: {
                if (_activeBindGp != null && _activeBindGp.joystickId == e.jbutton.which)
                {
                    if (_activeBtnBind != null)
                    {
                        _activeBtnBind.axis = SDL.SDL_GamepadAxis.SDL_GAMEPAD_AXIS_INVALID;
                        _activeBtnBind.button = (SDL.SDL_GamepadButton)e.jbutton.button;
                        _activeBtnBind = null;
                        _activeBindGp = null;
                    }
                }
                break;
            }
            case SDL.SDL_EventType.SDL_EVENT_GAMEPAD_AXIS_MOTION: {
                if (_activeBindGp != null && _activeBindGp.joystickId == e.jaxis.which)
                {
                    if (_activeBtnBind != null)
                    {
                        _activeBtnBind.axis = (SDL.SDL_GamepadAxis)e.jaxis.axis;
                        _activeBtnBind.invertAxis = e.jaxis.value < 0;
                        _activeBtnBind = null;
                        _activeBindGp = null;
                    }
                    else if (_activeAxisBind != null)
                    {
                        _activeAxisBind.axis = (SDL.SDL_GamepadAxis)e.jaxis.axis;
                        _activeAxisBind.invert = e.jaxis.value < 0;
                        _activeAxisBind = null;
                        _activeBindGp = null;
                    }
                }
                break;
            }
            case SDL.SDL_EventType.SDL_EVENT_KEY_DOWN: {
                if (e.key.key == (uint)SDL.SDL_Keycode.SDLK_ESCAPE)
                {
                    _activeBindGp = null;
                    _activeAxisBind = null;
                    _activeBtnBind = null;
                    _activeKbBtnBind = null;
                }
                else if (_activeKbBtnBind != null)
                {
                    _activeKbBtnBind.button = e.key.scancode;
                    _activeKbBtnBind = null;
                }
                break;
            }
        }
    }

    protected override void DrawContents()
    {
        base.DrawContents();

        string[] gamepadDevices = new string[_inputSystem.availableDevices.Count];
        for (int i = 0; i < _inputSystem.availableDevices.Count; i++)
        {
            gamepadDevices[i] = _inputSystem.availableDevices[i]?.Name ?? "None";
        }

        for (int i = 0; i < 4; i++)
        {
            if (ImGui.CollapsingHeader($"Player {i + 1}"))
            {
                int idx = Array.IndexOf(gamepadDevices, _inputSystem.gamepads[i]?.Name ?? "None");

                if (ImGui.Combo($"Input Device##{i}", ref idx, gamepadDevices, gamepadDevices.Length))
                {
                    _inputSystem.gamepads[i] = _inputSystem.availableDevices[idx]?.CreateInstance(_config.Gamepads[i]);
                    _config.Gamepads[i].DeviceName = _inputSystem.availableDevices[idx]?.Name;
                }

                if (_inputSystem.gamepads[i] is KeyboardGamepad kb)
                {
                    DrawSettings(i, kb);
                    _config.Gamepads[i].Kb = kb.config;
                }
                else if (_inputSystem.gamepads[i] is SDLGamepad gp)
                {
                    DrawSettings(i, gp);
                    _config.Gamepads[i].Gp = gp.config;
                }
            }
        }
    }

    protected void DrawSettings(int player, KeyboardGamepad kb)
    {
        if (ImGui.BeginTable($"input##{player}", 2))
        {
            DrawBinding("A", player, kb.config.btnA);
            DrawBinding("B", player, kb.config.btnB);
            DrawBinding("X", player, kb.config.btnX);
            DrawBinding("Y", player, kb.config.btnY);
            DrawBinding("Dpad Up", player, kb.config.btnUp);
            DrawBinding("Dpad Down", player, kb.config.btnDown);
            DrawBinding("Dpad Left", player, kb.config.btnLeft);
            DrawBinding("Dpad Right", player, kb.config.btnRight);
            DrawBinding("L1", player, kb.config.btnL1);
            DrawBinding("L2", player, kb.config.btnL2);
            DrawBinding("L3", player, kb.config.btnL3);
            DrawBinding("R1", player, kb.config.btnR1);
            DrawBinding("R2", player, kb.config.btnR2);
            DrawBinding("R3", player, kb.config.btnR3);
            DrawBinding("Start", player, kb.config.btnStart);
            DrawBinding("Select", player, kb.config.btnSelect);
            DrawBinding("Left Stick X", player, kb.config.lx);
            DrawBinding("Left Stick Y", player, kb.config.ly);
            DrawBinding("Right Stick X", player, kb.config.rx);
            DrawBinding("Right Stick Y", player, kb.config.ry);
            ImGui.EndTable();
        }
    }

    protected void DrawSettings(int player, SDLGamepad gp)
    {
        if (ImGui.BeginTable($"input##{player}", 2))
        {
            DrawBinding("A", gp, player, gp.config.btnA);
            DrawBinding("B", gp, player, gp.config.btnB);
            DrawBinding("X", gp, player, gp.config.btnX);
            DrawBinding("Y", gp, player, gp.config.btnY);
            DrawBinding("Dpad Up", gp, player, gp.config.btnUp);
            DrawBinding("Dpad Down", gp, player, gp.config.btnDown);
            DrawBinding("Dpad Left", gp, player, gp.config.btnLeft);
            DrawBinding("Dpad Right", gp, player, gp.config.btnRight);
            DrawBinding("L1", gp, player, gp.config.btnL1);
            DrawBinding("L2", gp, player, gp.config.btnL2);
            DrawBinding("L3", gp, player, gp.config.btnL3);
            DrawBinding("R1", gp, player, gp.config.btnR1);
            DrawBinding("R2", gp, player, gp.config.btnR2);
            DrawBinding("R3", gp, player, gp.config.btnR3);
            DrawBinding("Start", gp, player, gp.config.btnStart);
            DrawBinding("Select", gp, player, gp.config.btnSelect);
            DrawBinding("Left Stick X", gp, player, gp.config.lx);
            DrawBinding("Left Stick Y", gp, player, gp.config.ly);
            DrawBinding("Right Stick X", gp, player, gp.config.rx);
            DrawBinding("Right Stick Y", gp, player, gp.config.ry);
            ImGui.EndTable();
        }
    }

    protected void DrawBinding(string title, int player, KeyboardAxisBinding axis)
    {
        DrawBinding($"{title} +", player, axis.positive);
        DrawBinding($"{title} -", player, axis.negative);
    }

    protected void DrawBinding(string title, int player, KeyboardButtonBinding btn)
    {
        if (btn == _activeKbBtnBind)
        {
            DrawListenBinding(title, player);
            return;
        }

        if (DrawBinding(title, player, btn.button))
        {
            _activeKbBtnBind = btn;
        }
    }

    protected void DrawBinding(string title, SDLGamepad gp, int player, GamepadButtonBinding button)
    {
        if (button == _activeBtnBind)
        {
            DrawListenBinding(title, player);
            return;
        }

        if (button.axis != SDL.SDL_GamepadAxis.SDL_GAMEPAD_AXIS_INVALID)
        {
            if (DrawBinding(title, player, button.axis, button.invertAxis))
            {
                _activeBindGp = gp;
                _activeBtnBind = button;
            }
        }
        else
        {
            if (DrawBinding(title, player, button.button))
            {
                _activeBindGp = gp;
                _activeBtnBind = button;
            }
        }
    }

    protected void DrawBinding(string title, SDLGamepad gp, int player, GamepadAxisBinding axis)
    {
        if (axis == _activeAxisBind)
        {
            DrawListenBinding(title, player);
            return;
        }

        if (DrawBinding(title, player, axis.axis, axis.invert))
        {
            _activeBindGp = gp;
            _activeAxisBind = axis;
        }
    }

    protected bool DrawBinding(string title, int player, SDL.SDL_Scancode key)
    {
        uint kc = SDL.SDL_GetKeyFromScancode(key, SDL.SDL_Keymod.SDL_KMOD_NONE, false);
        string keyname = SDL.SDL_GetKeyName(kc);

        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.Text($"{title}");
        ImGui.TableNextColumn();
        return ImGui.Button($"{keyname}##{title}{player}", new Vector2(150, ImGui.GetTextLineHeightWithSpacing()));
    }

    protected bool DrawBinding(string title, int player, SDL.SDL_GamepadButton btn)
    {
        string btnName = btn == SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_INVALID ? "(none)" : SDL.SDL_GetGamepadStringForButton(btn);

        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.Text($"{title}");
        ImGui.TableNextColumn();
        return ImGui.Button($"{btnName}##{title}{player}", new Vector2(150, ImGui.GetTextLineHeightWithSpacing()));
    }

    protected bool DrawBinding(string title, int player, SDL.SDL_GamepadAxis axis, bool invert)
    {
        string axisName = axis == SDL.SDL_GamepadAxis.SDL_GAMEPAD_AXIS_INVALID ? "(none)" : SDL.SDL_GetGamepadStringForAxis(axis);

        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.Text($"{title}");
        ImGui.TableNextColumn();
        return ImGui.Button($"{axisName} {(invert ? "-" : "+")}##{title}{player}", new Vector2(150, ImGui.GetTextLineHeightWithSpacing()));
    }

    protected void DrawListenBinding(string title, int player)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.Text($"{title}");
        ImGui.TableNextColumn();
        ImGui.Button($"Waiting...##{title}{player}", new Vector2(150, ImGui.GetTextLineHeightWithSpacing()));
    }
}