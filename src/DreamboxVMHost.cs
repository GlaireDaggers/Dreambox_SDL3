namespace DreamboxVM;

using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using CommandLine;
using DreamboxVM.Graphics;
using DreamboxVM.ImGuiRendering;
using DreamboxVM.VM;
using DreamboxVM.Windows;
using ImGuiNET;
using QoiSharp;
using SDL3;

// EXPERIMENTAL:
// A version of the Dreambox VM which splits execution into a host process & a child process
// This prevents the child VM from locking up the host VM when doing CPU intensive work

class DreamboxVMHost
{
    static readonly IntPtr _isoFilterNamePtr = StringToUtf8("Dreambox game disc (ISO)");
    static readonly IntPtr _isoFilterPatternPtr = StringToUtf8("iso");

    static DreamboxVMHost? _instance = null;

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
    }

    public readonly CLIOptions cliOptions;

    private readonly DreamboxConfig _config;
    private readonly nint _window;
    private readonly GraphicsDevice _graphicsDevice;
    private readonly ImGuiRenderer _imGuiRenderer;

    private readonly List<WindowBase> _windowStack = [];
    private readonly Queue<WindowBase> _closedWindows = [];

    private ScreenEffects _screenFx;

    private AnonymousPipeServerStream _inPipe;
    private AnonymousPipeServerStream _outPipe;
    private BinaryWriter _outPipeWriter;
    private Process _subProcess;
    private Thread _pipeThread;
    private bool _pipeStreamRunning = false;

    private bool _fbPixelCopyReady = false;
    private byte[] _fbPixelBuffer = new byte[VDP.SCREEN_WIDTH * VDP.SCREEN_HEIGHT * 4];
    private Texture _fbTexture;

    public DreamboxVMHost()
    {
        var args = Environment.GetCommandLineArgs();

        CLIOptions options = new CLIOptions();
        Parser.Default.ParseArguments<CLIOptions>(args)
            .WithParsed(o => {
                options = o;
            })
            .WithNotParsed(errors => {
                Console.WriteLine("Failed parsing commandline arguments - falling back to default options");
            });

        cliOptions = options;

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

        // load icon
        var icon = QoiDecoder.Decode(File.ReadAllBytes("icon.qoi"));
        
        unsafe
        {
            fixed (void* pixels = icon.Data)
            {
                var iconSurface = SDL.SDL_CreateSurfaceFrom(icon.Width, icon.Height, icon.Channels == QoiSharp.Codec.Channels.Rgb ? SDL.SDL_PixelFormat.SDL_PIXELFORMAT_RGB24 : SDL.SDL_PixelFormat.SDL_PIXELFORMAT_RGBA32,
                    (nint)pixels, icon.Channels == QoiSharp.Codec.Channels.Rgb ? icon.Width * 3 : icon.Width * 4);

                SDL.SDL_SetWindowIcon(_window, (nint)iconSurface);
                SDL.SDL_DestroySurface((nint)iconSurface);
            }
        }

        // create graphics device
        _graphicsDevice = new GraphicsDevice(_window);

        SDL.SDL_SetGPUAllowedFramesInFlight(_graphicsDevice.handle, 2);

        // create ImGui renderer
        _imGuiRenderer = new ImGuiRenderer(_graphicsDevice, _graphicsDevice.swapchainFormat);
        _imGuiRenderer.RebuildFontAtlas();

        _screenFx = new ScreenEffects(_graphicsDevice, _config);

        // texture which input fb pixels will be read into
        _fbTexture = new Texture(_graphicsDevice, SDL.SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_R8G8B8A8_UNORM, VDP.SCREEN_WIDTH, VDP.SCREEN_HEIGHT, 1, SDL.SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER);

        // create pipe server
        _inPipe = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);

        _outPipe = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
        _outPipeWriter = new BinaryWriter(_outPipe);

        // start comm thread
        _pipeStreamRunning = true;
        _pipeThread = new Thread(RunCommThread);
        _pipeThread.Start();

        // start child process
        Console.WriteLine("Starting child process...");

        _subProcess = new Process();
        _subProcess.StartInfo.FileName = "dotnet";
        _subProcess.StartInfo.Arguments = $"{args[0]} --subprocess --out-pipe-handle {_inPipe.GetClientHandleAsString()} --in-pipe-handle {_outPipe.GetClientHandleAsString()}";
        _subProcess.StartInfo.UseShellExecute = false;
        _subProcess.StartInfo.WorkingDirectory = Environment.CurrentDirectory;

        _subProcess.Start();
    }

    public void LoadISO(string path)
    {
        _outPipeWriter.Write((byte)0);
        _outPipeWriter.Write(path);
    }

    public void Run()
    {
        double lastTick = SDL.SDL_GetPerformanceCounter();
        double tickFreq = SDL.SDL_GetPerformanceFrequency();
        double frameInterval = 1.0 / 60.0;

        double frameAccum = 0.0;

        bool quit = false;
        while(!quit)
        {
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

            // upload received framebuffer pixels
            lock (_fbPixelBuffer)
            {
                if (_fbPixelCopyReady)
                {
                    nint copyPass = SDL.SDL_BeginGPUCopyPass(cmdBuf);
                    _fbTexture.SetData(copyPass, cmdBuf, _fbPixelBuffer.AsSpan(), 0, 0, 0, 0, 0, VDP.SCREEN_WIDTH, VDP.SCREEN_HEIGHT, 1, true);
                    SDL.SDL_EndGPUCopyPass(copyPass);
                    _fbPixelCopyReady = false;
                }   
            }

            // draw a frame
            if (swapchainTex != 0)
            {
                if (frameAccum >= frameInterval)
                {
                    _screenFx.ReceiveFrame(cmdBuf, _fbTexture);

                    int numFrames = (int)(frameAccum / frameInterval);
                    frameAccum -= numFrames * frameInterval;
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

                _screenFx.BlitToScreen(cmdBuf, finalPass, wScale, hScale);

            #if !ENABLE_STANDALONE_MODE
                // draw ImGui
                if (!_config.HideMenu)
                {
                    _imGuiRenderer.BeforeLayout((float)delta, swapchainWidth, swapchainHeight);
                    {
                        DrawUI(ref quit);
                    }
                    _imGuiRenderer.AfterLayout(cmdBuf, finalPass, swapchainWidth, swapchainHeight);
                }
            #endif

                SDL.SDL_EndGPURenderPass(finalPass);

                while (_closedWindows.TryDequeue(out var win))
                {
                    win.OnClose();
                    _windowStack.Remove(win);
                }
            }

            SDL.SDL_SubmitGPUCommandBuffer(cmdBuf);
        }

        // quit child process
        Console.WriteLine("Killing child process");
        _subProcess.Kill();
        _subProcess.Dispose();

        // save settings
        DreamboxConfig.SavePrefs(_config);

        // teardown
        _screenFx.Dispose();

        _graphicsDevice.WaitForIdle();
        _graphicsDevice.Dispose();

        SDL.SDL_DestroyWindow(_window);
        SDL.SDL_Quit();
    }

    // listens for messages from child process
    private void RunCommThread()
    {
        using BinaryReader reader = new(_inPipe!);

        byte[] fbPixels = new byte[VDP.SCREEN_WIDTH * VDP.SCREEN_HEIGHT * 4];

        while (_pipeStreamRunning)
        {
            byte cmdType = reader.ReadByte();

            if (cmdType == 0)
            {
                // framebuffer
                int rd = 0;
                while (rd < fbPixels.Length)
                {
                    rd += reader.Read(fbPixels.AsSpan()[rd..]);
                }

                lock (_fbPixelBuffer)
                {
                    fbPixels.CopyTo(_fbPixelBuffer, 0);
                    _fbPixelCopyReady = true;
                }
            }
        }
    }

    private unsafe void DrawUI(ref bool quit)
    {
        if (ImGui.BeginMainMenuBar())
        {
            if (ImGui.BeginMenu("Emulation"))
            {
                if (ImGui.MenuItem("Insert Game Disc (ISO)"))
                {
                    SDL.SDL_ShowOpenFileDialog(HandleOpenGameDialog, 0, _window, [
                        new () {
                            name = (byte*)_isoFilterNamePtr,
                            pattern = (byte*)_isoFilterPatternPtr
                        }
                    ], 1, null, false);
                }
                if (ImGui.MenuItem("Enable CD/DVD Driver"))
                {
                    _outPipeWriter.Write((byte)1);
                }
                if (ImGui.MenuItem("Eject Game Disc"))
                {
                    _outPipeWriter.Write((byte)2);
                }
                if (ImGui.MenuItem("Reset"))
                {
                    _outPipeWriter.Write((byte)3);
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
                                LoadISO(game);
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
                if (ImGui.MenuItem("Hide Menu", "F12", _config.HideMenu))
                {
                    _config.HideMenu = !_config.HideMenu;
                }
                if (ImGui.MenuItem("Disable Frameskips", "", _config.DisableFrameskips))
                {
                    _config.DisableFrameskips = !_config.DisableFrameskips;
                }
                if (ImGui.MenuItem("Skip BIOS", "", _config.SkipBIOS))
                {
                    _config.SkipBIOS = !_config.SkipBIOS;
                }
                if (ImGui.MenuItem("Fullscreen", "", _config.Fullscreen))
                {
                    _config.Fullscreen = !_config.Fullscreen;
                    SDL.SDL_SetWindowFullscreen(_window, _config.Fullscreen);
                    SDL.SDL_SyncWindow(_window);
                }
                ImGui.Separator();
                if (ImGui.MenuItem("Interlaced Video", "", _config.InterlacedVideo))
                {
                    _config.InterlacedVideo = !_config.InterlacedVideo;
                }
                if (ImGui.BeginMenu("Video Mode"))
                {
                    if (ImGui.MenuItem("Default", "", _config.VideoMode == DreamboxVideoMode.Default))
                    {
                        _config.VideoMode = DreamboxVideoMode.Default;
                    }
                    if (ImGui.MenuItem("VGA", "", _config.VideoMode == DreamboxVideoMode.VGA))
                    {
                        _config.VideoMode = DreamboxVideoMode.VGA;
                    }
                    if (ImGui.MenuItem("Composite", "", _config.VideoMode == DreamboxVideoMode.Composite))
                    {
                        _config.VideoMode = DreamboxVideoMode.Composite;
                    }
                    if (ImGui.MenuItem("S-Video", "", _config.VideoMode == DreamboxVideoMode.SVideo))
                    {
                        _config.VideoMode = DreamboxVideoMode.SVideo;
                    }
                    ImGui.EndMenu();
                }
                if (ImGui.BeginMenu("CRT Preset"))
                {
                    if (ImGui.MenuItem("None", "", _config.CrtPreset == DreamboxCrtPreset.None))
                    {
                        _config.CrtPreset = DreamboxCrtPreset.None;
                    }
                    if (ImGui.MenuItem("Curve", "", _config.CrtPreset == DreamboxCrtPreset.Curve))
                    {
                        _config.CrtPreset = DreamboxCrtPreset.Curve;
                    }
                    if (ImGui.MenuItem("Flat", "", _config.CrtPreset == DreamboxCrtPreset.Flat))
                    {
                        _config.CrtPreset = DreamboxCrtPreset.Flat;
                    }
                    if (ImGui.MenuItem("Trinitron", "", _config.CrtPreset == DreamboxCrtPreset.Trinitron))
                    {
                        _config.CrtPreset = DreamboxCrtPreset.Trinitron;
                    }
                    ImGui.EndMenu();
                }
                ImGui.Separator();
                if (ImGui.MenuItem("Input"))
                {
                    // _windowStack.Add(new InputSettingsWindow(_config, _inputSys));
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