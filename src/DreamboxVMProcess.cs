namespace DreamboxVM;

using System.IO.Pipes;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CommandLine;
using DreamboxVM.Graphics;
using DreamboxVM.VM;
using SDL3;

// EXPERIMENTAL:
// A version of the Dreambox VM which splits execution into a host process & a child process
// This prevents the child VM from locking up the host VM when doing CPU intensive work

class DreamboxVMSubprocess
{
    public VDP VdpInstance => _vdp;
    public AudioSystem AudioSystemInstance => _audioSys;

    public readonly SubprocessCLIOptions cliOptions;

    private readonly DreamboxConfig _config;
    private readonly nint _window;
    private readonly GraphicsDevice _graphicsDevice;

    private VDP _vdp;
    private AudioSystem _audioSys;
    private readonly InputSystem _inputSys;

    private Runtime _vm;

    private readonly MemoryCard _mca;
    private readonly MemoryCard _mcb;

    private bool _paused = false;

    private DiskDriverWrapper _disk;
    private IDiskDriver? _queueLoadDisk = null;

    private bool _debugWireframe = false;

    private PipeStream? _inPipeStream;
    private PipeStream? _outPipeStream;
    private BinaryWriter? _outPipeStreamWriter;
    private Thread? _pipeThread;

    private bool _queueReset = false;
    private bool _queueEject = false;
    private bool _pipeStreamRunning = false;

    public DreamboxVMSubprocess()
    {
        SubprocessCLIOptions options = new SubprocessCLIOptions();
        Parser.Default.ParseArguments<SubprocessCLIOptions>(Environment.GetCommandLineArgs())
            .WithParsed(o => {
                options = o;
            })
            .WithNotParsed(errors => {
                Console.WriteLine("Failed parsing commandline arguments - falling back to default options");
            });

        cliOptions = options;

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

        _window = SDL.SDL_CreateWindow("Dreambox VM", 32, 32, SDL.SDL_WindowFlags.SDL_WINDOW_HIDDEN);

        if (_window == 0)
        {
            throw new Exception("Failed to create window");
        }

        // create graphics device
        _graphicsDevice = new GraphicsDevice(_window);

        SDL.SDL_SetGPUAllowedFramesInFlight(_graphicsDevice.handle, 2);

        // create VM components
        _vdp = new VDP(_config, _graphicsDevice);
        _audioSys = new AudioSystem();
        _inputSys = new InputSystem(_config);

        _disk = new DiskDriverWrapper();

        if (_config.DefaultDiscDriver == DreamboxDiscDriver.CD)
        {
            _disk.SetDriver(CreateCDDriver());
        }
        else if (_config.DefaultDiscDriver == DreamboxDiscDriver.ISO)
        {
            _disk.SetDriver(new ISODiskDriver());
        }
        else
        {
            Console.WriteLine("Invalid CD driver (falling back to ISO driver)");
            _disk.SetDriver(new ISODiskDriver());
        }

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

        _vm = InitVM();

        if (!string.IsNullOrEmpty(cliOptions.InPipeHandle))
        {
            Console.WriteLine("Initializing comm input pipe");

            _pipeStreamRunning = true;

            _inPipeStream = new AnonymousPipeClientStream(PipeDirection.In, cliOptions.InPipeHandle);
            _pipeThread = new Thread(RunCommThread);
            _pipeThread.Start();
        }

        if (!string.IsNullOrEmpty(cliOptions.OutPipeHandle))
        {
            Console.WriteLine("Initializing comm output pipe");

            _outPipeStream = new AnonymousPipeClientStream(PipeDirection.Out, cliOptions.OutPipeHandle);
            _outPipeStreamWriter = new BinaryWriter(_outPipeStream);
        }
    }

    public void LoadISO(string path)
    {
        var disk = new ISODiskDriver();
        disk.Insert(File.OpenRead(path));

        // queue disk to be loaded on main thread
        lock (this)
        {
            _queueLoadDisk = disk;
        }
    }

    public void Run()
    {
        // needed for a hidden window to receive input
        SDL.SDL_SetHint(SDL.SDL_HINT_JOYSTICK_ALLOW_BACKGROUND_EVENTS, "1");

        double lastTick = SDL.SDL_GetPerformanceCounter();
        double tickFreq = SDL.SDL_GetPerformanceFrequency();
        double frameInterval = 1.0 / 60.0;

        double frameAccum = 0.0;

        int skipFrames = 0;

        nint prevSubmitFence = 0;

        byte[] fbPix = new byte[VDP.SCREEN_WIDTH * VDP.SCREEN_HEIGHT * 4];

        bool quit = false;
        while(!quit)
        {
            // process queued load disk
            lock (this)
            {
                if (_queueLoadDisk != null)
                {
                    _disk.SetDriver(_queueLoadDisk);
                    _queueLoadDisk = null;

                    if (_config.SkipBIOS)
                    {
                        ResetVM();   
                    }
                }
            }

            // process queued reset
            if (_queueReset)
            {
                ResetVM();
                _queueReset = false;
            }

            // process queued eject
            if (_queueEject)
            {
                _disk.Eject();
                _queueEject = false;
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
                
                _inputSys.HandleSdlEvent(e);
            }

            // if we have a command buffer fence, wait for the fence, read back VDP framebuffer, & send to host
            if (prevSubmitFence != 0)
            {
                SDL.SDL_WaitForGPUFences(_graphicsDevice.handle, true, [prevSubmitFence], 1);
                SDL.SDL_ReleaseGPUFence(_graphicsDevice.handle, prevSubmitFence);
                prevSubmitFence = 0;

                _vdp.ScreenTarget.ReadData(fbPix.AsSpan());
                SendFramebuffer(fbPix.AsSpan());
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

            bool queueRead = false;

            // draw a frame
            if (swapchainTex != 0)
            {
                if (frameAccum >= frameInterval)
                {
                    int numFrames = (int)(frameAccum / frameInterval);

                    if (!_paused)
                    {
                        if (skipFrames > 0)
                        {
                            skipFrames -= numFrames;
                            if (skipFrames < 0) skipFrames = 0;
                        }
                        else
                        {
                            _vdp.BeginFrame(cmdBuf);
                            _vm.Tick();
                            _vdp.EndFrame(out var vdpSkips);
                            skipFrames += vdpSkips;

                            // begin reading data back from screen buffer
                            nint copyPass = SDL.SDL_BeginGPUCopyPass(cmdBuf);
                            _vdp.ScreenTarget.GetData(copyPass, 0, 0, 0, 0, 0, VDP.SCREEN_WIDTH, VDP.SCREEN_HEIGHT, 1);
                            SDL.SDL_EndGPUCopyPass(copyPass);

                            queueRead = true;
                        }
                    }

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

                SDL.SDL_EndGPURenderPass(finalPass);
            }

            if (queueRead)
            {
                prevSubmitFence = SDL.SDL_SubmitGPUCommandBufferAndAcquireFence(cmdBuf);
            }
            else
            {
                SDL.SDL_SubmitGPUCommandBuffer(cmdBuf);
            }
        }

        _pipeStreamRunning = false;

        // teardown
        _vdp.Dispose();
        _audioSys.Dispose();

        _graphicsDevice.WaitForIdle();
        _graphicsDevice.Dispose();

        SDL.SDL_DestroyWindow(_window);
        SDL.SDL_Quit();
    }

    // listens for & executes commands from parent process
    private void RunCommThread()
    {
        using BinaryReader reader = new(_inPipeStream!);

        while (_pipeStreamRunning)
        {
            byte cmd = reader.ReadByte();

            // load ISO
            if (cmd == 0)
            {
                LoadISO(reader.ReadString());
            }
            // load CD
            else if (cmd == 1)
            {
                lock (this)
                {
                    _queueLoadDisk = CreateCDDriver();
                }
            }
            // eject CD
            else if (cmd == 2)
            {
                _queueEject = true;
            }
            // reset
            else if (cmd == 3)
            {
                _queueReset = true;
            }
        }
    }

    // send a framebuffer back to parent process
    private void SendFramebuffer(Span<byte> fbData)
    {
        if (_outPipeStreamWriter != null)
        {
            _outPipeStreamWriter.Write((byte)0);
            _outPipeStreamWriter.Write(fbData);
        }
    }

    private Runtime InitVM()
    {
    #if FORCE_PRECOMPILED_RUNTIME
        // init game VM
        return InitGameVM();
    #else
        if (_config.SkipBIOS && _disk.Inserted())
        {
            return InitGameVM();
        }
        else
        {
            return InitBIOSVM();
        }
    #endif
    }

    private void ResetVM()
    {
        _vm.Dispose();
        _vdp.Dispose();
        _audioSys.Dispose();
        
        _vdp = new VDP(_config, _graphicsDevice);
        _audioSys = new AudioSystem();

        _vdp.SetWireframe(_debugWireframe);

        _vm = InitVM();
    }

    private Runtime InitBIOSVM()
    {
    #if FORCE_PRECOMPILED_RUNTIME
        throw new NotImplementedException();
    #else
        // init VM with BIOS
        byte[] bios = File.ReadAllBytes("content/bios.wasm");
        return Runtime.CreateWasmRuntime(bios, _config, _disk, _mca, _mcb, _vdp, _audioSys, _inputSys, cliOptions.WasmDebug);
    #endif
    }

    private Runtime InitGameVM()
    {
    #if FORCE_PRECOMPILED_RUNTIME
        // load precompiled binary
        byte[] moduleBytes = File.ReadAllBytes("content/runtime.cwasm");
        return Runtime.CreatePrecompiledRuntime(moduleBytes, _config, _disk, _mca, _mcb, _vdp, _audioSys, _inputSys, cliOptions.WasmDebug);
    #else
        // load game binary from disk & init VM
        byte[] moduleBytes;
        using (var execStream = _disk.OpenRead("main.wasm"))
        using (var memStream = new MemoryStream())
        {
            execStream.CopyTo(memStream);
            moduleBytes = memStream.ToArray();
        }
        return Runtime.CreateWasmRuntime(moduleBytes, _config, _disk, _mca, _mcb, _vdp, _audioSys, _inputSys, cliOptions.WasmDebug);
    #endif
    }

    private IDiskDriver CreateCDDriver()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new WindowsCDDiskDriver();
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return new LinuxCDDiskDriver();
        }

        throw new NotImplementedException();
    }
}