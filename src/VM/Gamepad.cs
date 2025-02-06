using System.Text.Json.Serialization;
using SDL3;

namespace DreamboxVM.VM;

abstract class InputDevice : IDisposable
{
    public abstract string Name { get; }
    public abstract Gamepad CreateInstance(GamepadSettings settings);

    public virtual void Dispose()
    {
    }
}

class KeyboardInputDevice : InputDevice
{
    public override string Name => "Keyboard";

    public override Gamepad CreateInstance(GamepadSettings settings)
    {
        return new KeyboardGamepad() {
            config = settings.Kb
        };
    }
}

class GamepadInputDevice : InputDevice
{
    public override string Name => _name;

    public uint joystickId;
    private nint _gamepad;
    private string _name;

    public GamepadInputDevice(uint joystickId)
    {
        this.joystickId = joystickId;
        _gamepad = SDL.SDL_OpenGamepad(joystickId);
        _name = SDL.SDL_GetGamepadName(_gamepad);
    }

    public override void Dispose()
    {
        SDL.SDL_CloseGamepad(_gamepad);
    }

    public override Gamepad CreateInstance(GamepadSettings settings)
    {
        return new SDLGamepad(joystickId, _gamepad, _name) {
            config = settings.Gp
        };
    }
}

/// <summary>
/// Base class for a gamepad device
/// </summary>
abstract class Gamepad
{
    /// <summary>
    /// Enumeration of buttons (maps to bit index within button mask)
    /// </summary>
    public enum Button
    {
        BTN_A       = 0,
        BTN_B       = 1,
        BTN_X       = 2,
        BTN_Y       = 3,
        BTN_DUP     = 4,
        BTN_DDOWN   = 5,
        BTN_DLEFT   = 6,
        BTN_DRIGHT  = 7,
        BTN_L1      = 8,
        BTN_L2      = 9,
        BTN_L3      = 10,
        BTN_R1      = 11,
        BTN_R2      = 12,
        BTN_R3      = 13,
        BTN_START   = 14,
        BTN_SELECT  = 15,
    }

    /// <summary>
    /// Represents the state of a gamepad
    /// </summary>
    public struct State
    {
        /// <summary>
        /// Bitmask of pressed buttons
        /// </summary>
        public ushort buttons;

        /// <summary>
        /// Value of left stick X axis
        /// </summary>
        public short lx;

        /// <summary>
        /// Value of left stick Y axis
        /// </summary>
        public short ly;

        /// <summary>
        /// Value of right stick X axis
        /// </summary>
        public short rx;

        /// <summary>
        /// Value of right stick Y axis
        /// </summary>
        public short ry;
    }

    /// <summary>
    /// The name of the gamepad
    /// </summary>
    public abstract string Name { get; }

    /// <summary>
    /// Poll the state of the gamepad
    /// </summary>
    public abstract void PollState();

    /// <summary>
    /// Get the current state of this gamepad
    /// </summary>
    public abstract ref State GetState();

    /// <summary>
    /// Sets the vibration state of the controller
    /// </summary>
    /// <param name="enable">Whether vibration is enabled or not</param>
    public abstract void SetRumble(bool enable);
}

/// <summary>
/// Basic dummy gamepad which provides no input
/// </summary>
class DummyGamepad : Gamepad
{
    public override string Name => "Dummy";

    private State state = new State();

    public override void PollState()
    {
    }

    public override ref State GetState()
    {
        return ref state;
    }

    public override void SetRumble(bool enable)
    {
    }
}

class KeyboardButtonBinding
{
    public SDL.SDL_Scancode button { get; set; }

    [JsonConstructor]
    public KeyboardButtonBinding()
    {
    }

    public KeyboardButtonBinding(SDL.SDL_Scancode button)
    {
        this.button = button;
    }

    public int GetButton(Span<byte> keys)
    {
        return keys[(int)button];
    }
}

class KeyboardAxisBinding
{
    public KeyboardButtonBinding positive { get; set; }
    public KeyboardButtonBinding negative { get; set; }

    [JsonConstructor]
    public KeyboardAxisBinding()
    {
        positive = new KeyboardButtonBinding(SDL.SDL_Scancode.SDL_SCANCODE_UNKNOWN);
        negative = new KeyboardButtonBinding(SDL.SDL_Scancode.SDL_SCANCODE_UNKNOWN);
    }

    public KeyboardAxisBinding(SDL.SDL_Scancode positive, SDL.SDL_Scancode negative)
    {
        this.positive = new KeyboardButtonBinding(positive);
        this.negative = new KeyboardButtonBinding(negative);
    }

    public short GetAxis(Span<byte> keys)
    {
        if (positive.GetButton(keys) != 0)
        {
            return short.MaxValue;
        }
        if (negative.GetButton(keys) != 0)
        {
            return short.MinValue;
        }
        return 0;
    }
}

class KeyboardConfig
{
    public KeyboardButtonBinding btnA { get; set; } = new(SDL.SDL_Scancode.SDL_SCANCODE_L);
    public KeyboardButtonBinding btnB { get; set; } = new (SDL.SDL_Scancode.SDL_SCANCODE_P);
    public KeyboardButtonBinding btnX { get; set; } = new (SDL.SDL_Scancode.SDL_SCANCODE_K);
    public KeyboardButtonBinding btnY { get; set; } = new (SDL.SDL_Scancode.SDL_SCANCODE_O);
    public KeyboardButtonBinding btnUp { get; set; } = new (SDL.SDL_Scancode.SDL_SCANCODE_UP);
    public KeyboardButtonBinding btnDown { get; set; } = new (SDL.SDL_Scancode.SDL_SCANCODE_DOWN);
    public KeyboardButtonBinding btnLeft { get; set; } = new (SDL.SDL_Scancode.SDL_SCANCODE_LEFT);
    public KeyboardButtonBinding btnRight { get; set; } = new (SDL.SDL_Scancode.SDL_SCANCODE_RIGHT);
    public KeyboardButtonBinding btnL1 { get; set; } = new (SDL.SDL_Scancode.SDL_SCANCODE_Q);
    public KeyboardButtonBinding btnL2 { get; set; } = new (SDL.SDL_Scancode.SDL_SCANCODE_LSHIFT);
    public KeyboardButtonBinding btnL3 { get; set; } = new (SDL.SDL_Scancode.SDL_SCANCODE_LCTRL);
    public KeyboardButtonBinding btnR1 { get; set; } = new (SDL.SDL_Scancode.SDL_SCANCODE_R);
    public KeyboardButtonBinding btnR2 { get; set; } = new (SDL.SDL_Scancode.SDL_SCANCODE_RSHIFT);
    public KeyboardButtonBinding btnR3 { get; set; } = new (SDL.SDL_Scancode.SDL_SCANCODE_RCTRL);
    public KeyboardButtonBinding btnStart { get; set; } = new (SDL.SDL_Scancode.SDL_SCANCODE_RETURN);
    public KeyboardButtonBinding btnSelect { get; set; } = new (SDL.SDL_Scancode.SDL_SCANCODE_SPACE);
    public KeyboardAxisBinding lx { get; set; } = new (SDL.SDL_Scancode.SDL_SCANCODE_D, SDL.SDL_Scancode.SDL_SCANCODE_A);
    public KeyboardAxisBinding ly { get; set; } = new (SDL.SDL_Scancode.SDL_SCANCODE_W, SDL.SDL_Scancode.SDL_SCANCODE_S);
    public KeyboardAxisBinding rx { get; set; } = new (SDL.SDL_Scancode.SDL_SCANCODE_H, SDL.SDL_Scancode.SDL_SCANCODE_F);
    public KeyboardAxisBinding ry { get; set; } = new (SDL.SDL_Scancode.SDL_SCANCODE_T, SDL.SDL_Scancode.SDL_SCANCODE_G);
}

/// <summary>
/// Maps keyboard controls to gamepad
/// </summary>
class KeyboardGamepad : Gamepad
{
    public KeyboardConfig config = new KeyboardConfig();

    public override string Name => "Keyboard";

    private State _state = new State();

    public override void PollState()
    {
        var keyPtr = SDL.SDL_GetKeyboardState(out var numKeys);

        unsafe
        {
            var keys = new Span<byte>((void*)keyPtr, numKeys);

            _state.buttons = 0;
            _state.lx = 0;
            _state.ly = 0;
            _state.rx = 0;
            _state.ry = 0;

            _state.buttons |= (ushort)(config.btnA.GetButton(keys) << (int)Button.BTN_A);
            _state.buttons |= (ushort)(config.btnB.GetButton(keys) << (int)Button.BTN_B);
            _state.buttons |= (ushort)(config.btnX.GetButton(keys) << (int)Button.BTN_X);
            _state.buttons |= (ushort)(config.btnY.GetButton(keys) << (int)Button.BTN_Y);
            _state.buttons |= (ushort)(config.btnUp.GetButton(keys) << (int)Button.BTN_DUP);
            _state.buttons |= (ushort)(config.btnDown.GetButton(keys) << (int)Button.BTN_DDOWN);
            _state.buttons |= (ushort)(config.btnLeft.GetButton(keys) << (int)Button.BTN_DLEFT);
            _state.buttons |= (ushort)(config.btnRight.GetButton(keys) << (int)Button.BTN_DRIGHT);
            _state.buttons |= (ushort)(config.btnL1.GetButton(keys) << (int)Button.BTN_L1);
            _state.buttons |= (ushort)(config.btnL2.GetButton(keys) << (int)Button.BTN_L2);
            _state.buttons |= (ushort)(config.btnL3.GetButton(keys) << (int)Button.BTN_L3);
            _state.buttons |= (ushort)(config.btnR1.GetButton(keys) << (int)Button.BTN_R1);
            _state.buttons |= (ushort)(config.btnR2.GetButton(keys) << (int)Button.BTN_R2);
            _state.buttons |= (ushort)(config.btnR3.GetButton(keys) << (int)Button.BTN_R3);
            _state.buttons |= (ushort)(config.btnStart.GetButton(keys) << (int)Button.BTN_START);
            _state.buttons |= (ushort)(config.btnSelect.GetButton(keys) << (int)Button.BTN_SELECT);
            _state.lx = config.lx.GetAxis(keys);
            _state.ly = config.ly.GetAxis(keys);
            _state.rx = config.rx.GetAxis(keys);
            _state.ry = config.ry.GetAxis(keys);
        }
    }

    public override ref State GetState()
    {
        return ref _state;
    }

    public override void SetRumble(bool enable)
    {
    }
}

public class GamepadButtonBinding
{
    public const int AXIS_THRESHOLD = 256;

    public bool invertAxis { get; set; }
    public SDL.SDL_GamepadButton button { get; set; }
    public SDL.SDL_GamepadAxis axis { get; set; }

    [JsonConstructor]
    public GamepadButtonBinding()
    {
    }

    public GamepadButtonBinding(SDL.SDL_GamepadButton button)
    {
        this.button = button;
        this.axis = SDL.SDL_GamepadAxis.SDL_GAMEPAD_AXIS_INVALID;
    }

    public GamepadButtonBinding(SDL.SDL_GamepadAxis axis)
    {
        this.button = SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_INVALID;
        this.axis = axis;
    }

    public bool GetButton(nint gamepad)
    {
        if (axis != SDL.SDL_GamepadAxis.SDL_GAMEPAD_AXIS_INVALID)
        {
            if (invertAxis)
            {
                return SDL.SDL_GetGamepadAxis(gamepad, axis) <= -AXIS_THRESHOLD;
            }
            else
            {
                return SDL.SDL_GetGamepadAxis(gamepad, axis) >= AXIS_THRESHOLD;
            }
        }
        else
        {
            return SDL.SDL_GetGamepadButton(gamepad, button);
        }
    }
}

public class GamepadAxisBinding
{
    public bool invert { get; set; }
    public SDL.SDL_GamepadAxis axis { get; set; }

    [JsonConstructor]
    public GamepadAxisBinding()
    {
    }

    public GamepadAxisBinding(SDL.SDL_GamepadAxis axis, bool invert = false)
    {
        this.invert = invert;
        this.axis = axis;
    }

    public short GetAxis(nint gamepad)
    {
        short val = 0;

        if (axis != SDL.SDL_GamepadAxis.SDL_GAMEPAD_AXIS_INVALID)
        {
            val = SDL.SDL_GetGamepadAxis(gamepad, axis);
        }

        if (invert)
        {
            return (short)-(val + 1);
        }
        else
        {
            return val;
        }
    }
}

class GamepadConfig
{
    public GamepadButtonBinding btnA { get; set; } = new GamepadButtonBinding(SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_SOUTH);
    public GamepadButtonBinding btnB { get; set; } = new GamepadButtonBinding(SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_EAST);
    public GamepadButtonBinding btnX { get; set; } = new GamepadButtonBinding(SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_WEST);
    public GamepadButtonBinding btnY { get; set; } = new GamepadButtonBinding(SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_NORTH);
    public GamepadButtonBinding btnUp { get; set; } = new GamepadButtonBinding(SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_UP);
    public GamepadButtonBinding btnDown { get; set; } = new GamepadButtonBinding(SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_DOWN);
    public GamepadButtonBinding btnLeft { get; set; } = new GamepadButtonBinding(SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_LEFT);
    public GamepadButtonBinding btnRight { get; set; } = new GamepadButtonBinding(SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_RIGHT);
    public GamepadButtonBinding btnL1 { get; set; } = new GamepadButtonBinding(SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_LEFT_SHOULDER);
    public GamepadButtonBinding btnL2 { get; set; } = new GamepadButtonBinding(SDL.SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFT_TRIGGER);
    public GamepadButtonBinding btnL3 { get; set; } = new GamepadButtonBinding(SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_LEFT_STICK);
    public GamepadButtonBinding btnR1 { get; set; } = new GamepadButtonBinding(SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_RIGHT_SHOULDER);
    public GamepadButtonBinding btnR2 { get; set; } = new GamepadButtonBinding(SDL.SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHT_TRIGGER);
    public GamepadButtonBinding btnR3 { get; set; } = new GamepadButtonBinding(SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_RIGHT_STICK);
    public GamepadButtonBinding btnStart { get; set; } = new GamepadButtonBinding(SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_START);
    public GamepadButtonBinding btnSelect { get; set; } = new GamepadButtonBinding(SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_BACK);
    public GamepadAxisBinding lx { get; set; } = new GamepadAxisBinding(SDL.SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTX);
    public GamepadAxisBinding ly { get; set; } = new GamepadAxisBinding(SDL.SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTY, true);
    public GamepadAxisBinding rx { get; set; } = new GamepadAxisBinding(SDL.SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHTX);
    public GamepadAxisBinding ry { get; set; } = new GamepadAxisBinding(SDL.SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHTY, true);
}

/// <summary>
/// Maps an SDL gamepad to a virtual gamepad
/// </summary>
class SDLGamepad : Gamepad, IDisposable
{
    public override string Name => _name;

    public GamepadConfig config { get; set; } = new ();

    public readonly uint joystickId;

    private readonly string _name;
    private readonly nint _gamepad;
    private State _state = new();

    public SDLGamepad(uint joystickId, nint gamepad, string name)
    {
        this.joystickId = joystickId;
        _gamepad = gamepad;
        _name = name;
    }

    public override void PollState()
    {
        _state.buttons = 0;
        _state.lx = 0;
        _state.ly = 0;
        _state.rx = 0;
        _state.ry = 0;

        _state.buttons |= (ushort)(GetButton(config.btnA) << (int)Button.BTN_A);
        _state.buttons |= (ushort)(GetButton(config.btnB) << (int)Button.BTN_B);
        _state.buttons |= (ushort)(GetButton(config.btnX) << (int)Button.BTN_X);
        _state.buttons |= (ushort)(GetButton(config.btnY) << (int)Button.BTN_Y);
        _state.buttons |= (ushort)(GetButton(config.btnUp) << (int)Button.BTN_DUP);
        _state.buttons |= (ushort)(GetButton(config.btnDown) << (int)Button.BTN_DDOWN);
        _state.buttons |= (ushort)(GetButton(config.btnLeft) << (int)Button.BTN_DLEFT);
        _state.buttons |= (ushort)(GetButton(config.btnRight) << (int)Button.BTN_DRIGHT);
        _state.buttons |= (ushort)(GetButton(config.btnL1) << (int)Button.BTN_L1);
        _state.buttons |= (ushort)(GetButton(config.btnL2) << (int)Button.BTN_L2);
        _state.buttons |= (ushort)(GetButton(config.btnL3) << (int)Button.BTN_L3);
        _state.buttons |= (ushort)(GetButton(config.btnR1) << (int)Button.BTN_R1);
        _state.buttons |= (ushort)(GetButton(config.btnR2) << (int)Button.BTN_R2);
        _state.buttons |= (ushort)(GetButton(config.btnR3) << (int)Button.BTN_R3);
        _state.buttons |= (ushort)(GetButton(config.btnStart) << (int)Button.BTN_START);
        _state.buttons |= (ushort)(GetButton(config.btnSelect) << (int)Button.BTN_SELECT);

        _state.lx = config.lx.GetAxis(_gamepad);
        _state.ly = config.ly.GetAxis(_gamepad);
        _state.rx = config.rx.GetAxis(_gamepad);
        _state.ry = config.ry.GetAxis(_gamepad);
    }

    public override ref State GetState()
    {
        return ref _state;
    }

    public void Dispose()
    {
        SDL.SDL_CloseGamepad(_gamepad);
    }

    private int GetButton(GamepadButtonBinding btn)
    {
        return btn.GetButton(_gamepad) ? 1 : 0;
    }

    public override void SetRumble(bool enable)
    {
        // TODO
    }
}