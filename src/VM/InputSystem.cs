using DreamboxVM;
using DreamboxVM.VM;
using SDL3;

class InputSystem
{
    public Gamepad?[] gamepads = new Gamepad[4];
    public List<InputDevice?> availableDevices = [null, new KeyboardInputDevice()];

    private DreamboxConfig _config;

    public InputSystem(DreamboxConfig config)
    {
        _config = config;
    }

    public void HandleSdlEvent(in SDL.SDL_Event e)
    {
        if (e.type == (uint)SDL.SDL_EventType.SDL_EVENT_GAMEPAD_ADDED)
        {
            var gamepad = new GamepadInputDevice(e.gdevice.which);
            availableDevices.Add(gamepad);
            Console.WriteLine("Controller connected: " + gamepad.Name);

            // if new controller matches one defined in config, auto-assign to that slot
            for (int i = 0; i < _config.Gamepads.Length; i++)
            {
                if (gamepad.Name == _config.Gamepads[i].DeviceName)
                {
                    gamepads[i] = gamepad.CreateInstance(_config.Gamepads[i]);
                    Console.WriteLine($"Assigned new controller to slot {i}");
                    break;
                }
            }
        }
        else if (e.type == (uint)SDL.SDL_EventType.SDL_EVENT_GAMEPAD_REMOVED)
        {
            for (int i = 0; i < availableDevices.Count; i++)
            {
                if (availableDevices[i] is GamepadInputDevice gp && gp.joystickId == e.gdevice.which)
                {
                    Console.WriteLine("Controller disconnected: " + gp.Name);
                    for (int slot = 0; slot < gamepads.Length; slot++)
                    {
                        if (gamepads[slot] is SDLGamepad sdlPad && sdlPad.joystickId == gp.joystickId)
                        {
                            Console.WriteLine($"Removed controller from slot {slot}");
                            gamepads[slot] = null;
                        }
                    }
                    gp.Dispose();
                    availableDevices.RemoveAt(i);
                    break;
                }
            }
        }
    }
}