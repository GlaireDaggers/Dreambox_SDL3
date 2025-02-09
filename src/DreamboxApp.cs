namespace DreamboxVM;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using DreamboxVM.Graphics;
using DreamboxVM.ImGuiRendering;
using DreamboxVM.VM;
using DreamboxVM.Windows;
using ImGuiNET;
using SDL3;

class DreamboxApp
{
    static readonly IntPtr _isoFilterNamePtr = StringToUtf8("Dreambox game disc (ISO)");
    static readonly IntPtr _isoFilterPatternPtr = StringToUtf8("iso");

    static DreamboxApp? _instance = null;

    static IntPtr StringToUtf8(string str)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(str);
        IntPtr ptr = Marshal.AllocHGlobal(bytes.Length + 1);
        Marshal.Copy(bytes, 0, ptr, bytes.Length);
        Marshal.WriteByte(ptr + bytes.Length, 0);

        return ptr;
    }

    static unsafe void HandleOpenGameDialog(nint userdata, nint filelist, int filter)
    {
        if (filelist == 0)
        {
            // error
            Console.WriteLine(SDL.SDL_GetError());
        }
        else
        {
            // pointer to a list of string pointers
            nint* ptrs = (nint*)filelist;

            if (*ptrs == 0)
            {
                // user either didn't choose a file or cancelled the dialog
                Console.WriteLine("Dialog cancelled");
            }
            else
            {
                nint file = *ptrs;
                string filename = Marshal.PtrToStringUTF8(file)!;
                Console.WriteLine($"Chose: {filename}");

                _instance!.LoadISO(filename);
                _instance!._config.AddGame(filename);
            }
        }

        // resume
        _instance!._paused = false;
    }

    private readonly DreamboxConfig _config;
    private readonly nint _window;
    private readonly GraphicsDevice _graphicsDevice;
    private readonly ImGuiRenderer _imGuiRenderer;

    private readonly List<WindowBase> _windowStack = [];
    private readonly Queue<WindowBase> _closedWindows = [];

    private readonly VDP _vdp;
    private readonly AudioSystem _audioSys;
    private readonly InputSystem _inputSys;

    private Runtime? _vm = null;

    private readonly MemoryCard _mca;
    private readonly MemoryCard _mcb;

    private bool _paused = false;

    private bool _debug = false;

    private IDiskDriver? _queueLoadDisk = null;

    public DreamboxApp(bool debug)
    {
        _debug = debug;
        _instance = this;

        // load config
        _config = DreamboxConfig.LoadPrefs();
        
        var flags =
            SDL.SDL_InitFlags.SDL_INIT_VIDEO |
            SDL.SDL_InitFlags.SDL_INIT_AUDIO |
            SDL.SDL_InitFlags.SDL_INIT_GAMEPAD;

        if (!SDL.SDL_Init(flags))
        {
            throw new Exception("Failed to initialize SDL");
        }

        _window = SDL.SDL_CreateWindow("Dreambox VM", 960, 720, SDL.SDL_WindowFlags.SDL_WINDOW_RESIZABLE);

        if (_window == 0)
        {
            throw new Exception("Failed to create window");
        }

        // create graphics device
        _graphicsDevice = new GraphicsDevice(_window);

        SDL.SDL_SetGPUAllowedFramesInFlight(_graphicsDevice.handle, 2);

        // create ImGui renderer
        _imGuiRenderer = new ImGuiRenderer(_graphicsDevice, _graphicsDevice.swapchainFormat);
        _imGuiRenderer.RebuildFontAtlas();

        // create VM components
        _vdp = new VDP(_graphicsDevice);
        _audioSys = new AudioSystem();
        _inputSys = new InputSystem(_config);

        _mca = new MemoryCard(PathUtils.GetPath("memcard_a.sav"));
        _mcb = new MemoryCard(PathUtils.GetPath("memcard_b.sav"));

        // assign keyboard to configured gamepad slots on startup
        for (int i = 0; i < _config.Gamepads.Length; i++)
        {
            if (_config.Gamepads[i].DeviceName == "Keyboard")
            {
                _inputSys.gamepads[i] = new KeyboardGamepad() {
                    config = _config.Gamepads[i].Kb
                };
            }
        }
    }

    public void LoadISO(string path)
    {
        var disk = new ISODiskDriver();
        disk.Insert(File.OpenRead(path));

        // queue disk to be loaded on main thread
        _queueLoadDisk = disk;
    }

    public void Run()
    {
        double lastTick = SDL.SDL_GetPerformanceCounter();
        double tickFreq = SDL.SDL_GetPerformanceFrequency();
        double frameInterval = 1.0 / 60.0;

        double frameAccum = 0.0;

        int skipFrames = 0;

        bool quit = false;
        while(!quit)
        {
            // process queued load disk
            lock (this)
            {
                if (_queueLoadDisk != null)
                {
                    try
                    {
                        _vm = new Runtime(_config, _queueLoadDisk, _mca, _mcb, _vdp, _audioSys, _inputSys, _debug);
                        _vm.Start();
                    }
                    catch(Exception e)
                    {
                        Console.WriteLine("Failed loading game disk: " + e.Message);
                    }
                    _queueLoadDisk = null;
                }
            }

            double curTick = SDL.SDL_GetPerformanceCounter();
            double delta = (curTick - lastTick) / tickFreq;
            lastTick = curTick;

            if (delta > frameInterval * 4) {
                delta = frameInterval * 4;
            }

            frameAccum += delta;

            while (SDL.SDL_PollEvent(out SDL.SDL_Event e))
            {
                if (e.type == (uint)SDL.SDL_EventType.SDL_EVENT_QUIT)
                {
                    quit = true;
                }
                else if (e.type == (uint)SDL.SDL_EventType.SDL_EVENT_KEY_DOWN)
                {
                    // F12 toggles show/hide menu bar
                    if (e.key.key == (uint)SDL.SDL_Keycode.SDLK_F12)
                    {
                        _config.HideMenu = !_config.HideMenu;
                    }
                }
                
                _inputSys.HandleSdlEvent(e);
                ImGuiRenderer.HandleEvent(e);

                foreach (var win in _windowStack)
                {
                    win.HandleEvent(e);
                }
            }

            // acquire command buffer
            nint cmdBuf = SDL.SDL_AcquireGPUCommandBuffer(_graphicsDevice.handle);
            if (cmdBuf == 0)
            {
                throw new Exception("Failed acquiring command buffer: " + SDL.SDL_GetError());
            }

            if (!SDL.SDL_AcquireGPUSwapchainTexture(cmdBuf, _window, out var swapchainTex, out var swapchainWidth, out var swapchainHeight))
            {
                throw new Exception("Failed acquiring swapchain texture: " + SDL.SDL_GetError());
            }

            // draw a frame
            if (swapchainTex != 0)
            {
                while (frameAccum >= frameInterval)
                {
                    if (!_paused) {
                        if (skipFrames > 0) {
                            skipFrames--;
                        }
                        else {
                            _vdp.BeginFrame(cmdBuf);
                            {
                                _vm?.Tick();
                            }
                            _vdp.EndFrame(out var vdpSkips);
                            skipFrames += vdpSkips;
                        }
                    }

                    frameAccum -= frameInterval;
                }

                var finalPass = SDL.SDL_BeginGPURenderPass(cmdBuf, [new ()
                    {
                        texture = swapchainTex,
                        load_op = SDL.SDL_GPULoadOp.SDL_GPU_LOADOP_CLEAR,
                        store_op =  SDL.SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE
                    }],
                    1,
                    Unsafe.NullRef<SDL.SDL_GPUDepthStencilTargetInfo>());
                
                SDL.SDL_SetGPUViewport(finalPass, new () {
                    x = 0,
                    y = 0,
                    w = swapchainWidth,
                    h = swapchainHeight,
                    min_depth = 0.0f,
                    max_depth = 1.0f                    
                });

                // fit VM image to screen
                float ratioW = swapchainWidth / (float)VDP.SCREEN_WIDTH;
                float ratioH = swapchainHeight / (float)VDP.SCREEN_HEIGHT;
                float ratio = MathF.Min(ratioW, ratioH);
                
                float wScale = VDP.SCREEN_WIDTH * ratio / swapchainWidth;
                float hScale = VDP.SCREEN_HEIGHT * ratio / swapchainHeight;

                _vdp.BlitToScreen(finalPass, wScale, hScale);

                // draw ImGui
                if (!_config.HideMenu)
                {
                    _imGuiRenderer.BeforeLayout((float)delta, swapchainWidth, swapchainHeight);
                    {
                        DrawUI(ref quit);
                    }
                    _imGuiRenderer.AfterLayout(cmdBuf, finalPass, swapchainWidth, swapchainHeight);
                }

                SDL.SDL_EndGPURenderPass(finalPass);

                while (_closedWindows.TryDequeue(out var win))
                {
                    win.OnClose();
                    _windowStack.Remove(win);
                }
            }

            SDL.SDL_SubmitGPUCommandBuffer(cmdBuf);
        }

        // save settings
        DreamboxConfig.SavePrefs(_config);

        // teardown
        _vdp.Dispose();
        _audioSys.Dispose();

        _graphicsDevice.WaitForIdle();
        _graphicsDevice.Dispose();

        SDL.SDL_DestroyWindow(_window);
        SDL.SDL_Quit();
    }

    private unsafe void DrawUI(ref bool quit)
    {
        if (ImGui.BeginMainMenuBar())
        {
            if (ImGui.BeginMenu("Emulation"))
            {
                if (ImGui.MenuItem("Open Game (ISO)"))
                {
                    SDL.SDL_ShowOpenFileDialog(HandleOpenGameDialog, 0, _window, [
                        new () {
                            name = (byte*)_isoFilterNamePtr,
                            pattern = (byte*)_isoFilterPatternPtr
                        }
                    ], 1, null, false);
                    _paused = true;
                }
                if (ImGui.MenuItem("Open Game (CD/DVD)"))
                {
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        _queueLoadDisk = new WindowsCDDiskDriver();
                    }
                    else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    {
                        _queueLoadDisk = new LinuxCDDiskDriver();
                    }
                }
                if (_config.RecentGames.Count == 0)
                {
                    ImGui.BeginDisabled();
                    ImGui.MenuItem("Recent Games");
                    ImGui.EndDisabled();
                }
                else
                {
                    if (ImGui.BeginMenu("Recent Games"))
                    {
                        for (int i = 0; i < _config.RecentGames.Count; i++)
                        {
                            var game = _config.RecentGames[i];
                            if (ImGui.MenuItem(game))
                            {
                                // todo
                                var disk = new ISODiskDriver();
                                disk.Insert(File.OpenRead(game));

                                _queueLoadDisk = disk;
                            }
                        }
                        ImGui.EndMenu();
                    }
                }
                ImGui.Separator();
                if (ImGui.MenuItem("Quit"))
                {
                    quit = true;
                }
                ImGui.EndMenu();
            }
            if (ImGui.BeginMenu("Settings"))
            {
                if (ImGui.MenuItem("Disable Frameskips", "", _config.DisableFrameskips))
                {
                    _config.DisableFrameskips = !_config.DisableFrameskips;
                }
                if (ImGui.MenuItem("Fullscreen", "", _config.Fullscreen))
                {
                    _config.Fullscreen = !_config.Fullscreen;
                    SDL.SDL_SetWindowFullscreen(_window, _config.Fullscreen);
                    SDL.SDL_SyncWindow(_window);
                }
                ImGui.Separator();
                if (ImGui.MenuItem("Input"))
                {
                    _windowStack.Add(new InputSettingsWindow(_config, _inputSys));
                }
                ImGui.EndMenu();
            }
            ImGui.EndMainMenuBar();
        }

        for (int i = 0; i < _windowStack.Count; i++)
        {
            bool open = true;
            _windowStack[i].DrawGui(ref open);
            if (!open)
            {
                _closedWindows.Enqueue(_windowStack[i]);
            }
        }
    }
}