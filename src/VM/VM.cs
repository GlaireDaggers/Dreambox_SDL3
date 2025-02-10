namespace DreamboxVM.VM;

using System.Numerics;
using System.Runtime.CompilerServices;
using SDL3;
using Wasmtime;

public delegate void VMLogOutputHandler(string message);

class Runtime : IDisposable
{
    public static event VMLogOutputHandler? OnLogOutput;

    private const int TOTAL_MEM = 256;

    private struct clock_dateTime
    {
        public ushort year;
        public byte month;
        public byte day;
        public byte hour;
        public byte minute;
        public byte second;
    }

    public readonly Engine engine;

    public Module? module;
    public Instance? moduleInstance;
    public Memory? memory;

    private Linker _linker;
    private Store _store;
    
    private readonly IDiskDriver _disk;
    private readonly MemoryCard _mca;
    private readonly MemoryCard _mcb;
    private readonly VDP _vdp;
    private readonly AudioSystem _audioSystem;
    private readonly InputSystem _inputSystem;

    private Stream?[] _files = new Stream?[32];
    private DirectoryReader?[] _dirs = new DirectoryReader?[32];

    private Matrix4x4 _mat4;

    private Func<int>? __errno_location;
    private Func<int>? _main;
    private Func<int, int>? _malloc;
    private Action<int>? _free;

    private Action? _vsync;

    private int _dirEntBuffer;
    private string _tableName = "";

    private PackedVertex[] _tmpPackedVertex = new PackedVertex[1024];
    private byte[] _tmpPackedVertexBufferData = new byte[1024 * 32];

    private DreamboxConfig _config;

    private Module? _queueLoadModule = null;

    #if !FORCE_PRECOMPILED_RUNTIME
    public static Runtime CreateWasmRuntime(Span<byte> moduleBytes, DreamboxConfig config, IDiskDriver disk, MemoryCard mca, MemoryCard mcb, VDP vdp, AudioSystem audioSystem, InputSystem inputSystem, bool debug = false)
    {
        Config wasmConfig = new Config()
            .WithWasmThreads(true)
            .WithBulkMemory(true)
            .WithSIMD(true)
            .WithRelaxedSIMD(true, false)
            .WithOptimizationLevel(debug ? OptimizationLevel.None : OptimizationLevel.Speed)
            .WithDebugInfo(debug);
        
        var engine = new Engine(wasmConfig);
        return new Runtime(engine, Module.FromBytes(engine, "main", moduleBytes), config, disk, mca, mcb, vdp, audioSystem, inputSystem, debug);
    }
    #endif

    public static Runtime CreatePrecompiledRuntime(Span<byte> moduleBytes, DreamboxConfig config, IDiskDriver disk, MemoryCard mca, MemoryCard mcb, VDP vdp, AudioSystem audioSystem, InputSystem inputSystem, bool debug = false)
    {
        Config wasmConfig = new Config()
            .WithWasmThreads(true)
            .WithBulkMemory(true)
            .WithSIMD(true)
            .WithRelaxedSIMD(true, false)
            .WithOptimizationLevel(debug ? OptimizationLevel.None : OptimizationLevel.Speed)
            .WithDebugInfo(debug);
        
        var engine = new Engine(wasmConfig);
        return new Runtime(engine, Module.Deserialize(engine, "main", moduleBytes), config, disk, mca, mcb, vdp, audioSystem, inputSystem, debug);
    }

    public Runtime(Engine engine, Module module, DreamboxConfig config, IDiskDriver disk, MemoryCard mca, MemoryCard mcb, VDP vdp, AudioSystem audioSystem, InputSystem inputSystem, bool debug = false)
    {
        _config = config;
        
        _disk = disk;
        _mca = mca;
        _mcb = mcb;
        _vdp = vdp;
        _audioSystem = audioSystem;
        _inputSystem = inputSystem;

        this.engine = engine;
        
        _linker = new Linker(engine);
        _store = new Store(engine);

        WasiConfiguration wasiConfig = new WasiConfiguration();
        _store.SetWasiConfiguration(wasiConfig);

        _linker.DefineFunction<int>("env", "db_log", db_log);
        _linker.DefineFunction<int>("env", "vdp_clearColor", vdp_clearColor);
        _linker.DefineFunction<float>("env", "vdp_clearDepth", vdp_clearDepth);
        _linker.DefineFunction<int>("env", "vdp_clearStencil", vdp_clearStencil);
        _linker.DefineFunction<int>("env", "vdp_depthWrite", vdp_depthWrite);
        _linker.DefineFunction<int>("env", "vdp_depthFunc", vdp_depthFunc);
        _linker.DefineFunction<int>("env", "vdp_blendEquation", vdp_blendEquation);
        _linker.DefineFunction<int, int>("env", "vdp_blendFunc", vdp_blendFunc);
        _linker.DefineFunction<int>("env", "vdp_setWinding", vdp_setWinding);
        _linker.DefineFunction<int>("env", "vdp_setCulling", vdp_setCulling);
        _linker.DefineFunction<int, int, int, int>("env", "vdp_drawGeometry", vdp_drawGeometry);
        _linker.DefineFunction<int, int, int, int>("env", "vdp_drawGeometryPacked", vdp_drawGeometryPacked);
        _linker.DefineFunction<int>("env", "vdp_setVsyncHandler", vdp_setVsyncHandler);
        _linker.DefineFunction<int, int, int, int, int>("env", "vdp_allocTexture", vdp_allocTexture);
        _linker.DefineFunction<int, int, int>("env", "vdp_allocRenderTexture", vdp_allocRenderTexture);
        _linker.DefineFunction<int>("env", "vdp_releaseTexture", vdp_releaseTexture);
        _linker.DefineFunction("env", "vdp_getUsage", vdp_getUsage);
        _linker.DefineFunction<int, int, int, int>("env", "vdp_setTextureData", vdp_textureData);
        _linker.DefineFunction<int, int, int, int, int, int, int>("env", "vdp_setTextureDataYUV", vdp_textureDataYUV);
        _linker.DefineFunction<int, int, int, int, int>("env", "vdp_setTextureDataRegion", vdp_textureDataRegion);
        _linker.DefineFunction<int, int, int>("env", "vdp_copyFbToTexture", vdp_copyFbToTexture);
        _linker.DefineFunction<int>("env", "vdp_setRenderTarget", vdp_setRenderTarget);
        _linker.DefineFunction<int, int, int>("env", "vdp_setSampleParams", vdp_setSampleParams);
        _linker.DefineFunction<int>("env", "vdp_bindTexture", vdp_bindTexture);
        _linker.DefineFunction<int, int, int, int>("env", "vdp_setSampleParamsSlot", vdp_setSampleParamsSlot);
        _linker.DefineFunction<int, int>("env", "vdp_bindTextureSlot", vdp_bindTextureSlot);
        _linker.DefineFunction<int, int>("env", "vdp_setTexCombine", vdp_setTexCombine);
        _linker.DefineFunction<int, int, int, int>("env", "vdp_viewport", vdp_viewport);
        _linker.DefineFunction<float, int, int, int, int, int>("env", "vdp_submitDepthQuery", vdp_submitDepthQuery);
        _linker.DefineFunction("env", "vdp_getDepthQueryResult", vdp_getDepthQueryResult);
        _linker.DefineFunction<int, int>("env", "vdp_setVUCData", vdp_setVUCData);
        _linker.DefineFunction<int, int>("env", "vdp_uploadVUProgram", vdp_uploadVUProgram);
        _linker.DefineFunction<int, int, int>("env", "vdp_setVULayout", vdp_setVULayout);
        _linker.DefineFunction<int>("env", "vdp_setVUStride", vdp_setVUStride);
        _linker.DefineFunction<int, int, int>("env", "vdp_submitVU", vdp_submitVU);
        _linker.DefineFunction<int>("env", "mat4_loadSIMD", mat_load);
        _linker.DefineFunction<int>("env", "mat4_storeSIMD", mat_store);
        _linker.DefineFunction<int>("env", "mat4_mulSIMD", mat_mul);
        _linker.DefineFunction<int, int, int, int>("env", "mat4_transformSIMD", mat_transform);
        _linker.DefineFunction<int, int, int, int>("env", "audio_alloc", audio_alloc);
        _linker.DefineFunction<int, int, int, int>("env", "audio_allocCompressed", audio_allocCompressed);
        _linker.DefineFunction<int>("env", "audio_free", audio_free);
        _linker.DefineFunction("env", "audio_getUsage", audio_getUsage);
        _linker.DefineFunction<int, int, int, double>("env", "audio_queueSetParam_i", audio_queueSetParam_i);
        _linker.DefineFunction<int, int, float, double>("env", "audio_queueSetParam_f", audio_queueSetParam_f);
        _linker.DefineFunction<int, double>("env", "audio_queueStartVoice", audio_queueStartVoice);
        _linker.DefineFunction<int, double>("env", "audio_queueStopVoice", audio_queueStopVoice);
        _linker.DefineFunction<int, int>("env", "audio_getVoiceState", audio_getVoiceState);
        _linker.DefineFunction("env", "audio_getTime", audio_getTime);
        _linker.DefineFunction<float, float, float, float, float>("env", "audio_setReverbParams", audio_setReverbParams);
        _linker.DefineFunction<int, int, int>("env", "audio_initSynth", audio_initSynth);
        _linker.DefineFunction<int, int, int, int>("env", "audio_playMidi", audio_playMidi);
        _linker.DefineFunction<int>("env", "audio_setMidiReverb", audio_setMidiReverb);
        _linker.DefineFunction<float>("env", "audio_setMidiVolume", audio_setMidiVolume);
        _linker.DefineFunction<int, int>("env", "gamepad_isConnected", gamepad_isConnected);
        _linker.DefineFunction<int, int>("env", "gamepad_readState", gamepad_readState);
        _linker.DefineFunction<int, int>("env", "gamepad_setRumble", gamepad_setRumble);
        _linker.DefineFunction<int, int>("env", "fs_deviceExists", fs_deviceExists);
        _linker.DefineFunction<int>("env", "fs_deviceEject", fs_deviceEject);
        _linker.DefineFunction<int, int>("env", "fs_fileExists", fs_fileExists);
        _linker.DefineFunction<int, int, int>("env", "fs_open", fs_open);
        _linker.DefineFunction<int, int, int, int>("env", "fs_read", fs_read);
        _linker.DefineFunction<int, int, int, int>("env", "fs_write", fs_write);
        _linker.DefineFunction<int>("env", "fs_flush", fs_flush);
        _linker.DefineFunction<int, int, int, int>("env", "fs_seek", fs_seek);
        _linker.DefineFunction<int, int>("env", "fs_tell", fs_tell);
        _linker.DefineFunction<int>("env", "fs_close", fs_close);
        _linker.DefineFunction<int, int>("env", "fs_eof", fs_eof);
        _linker.DefineFunction<int, int>("env", "fs_openDir", fs_opendir);
        _linker.DefineFunction<int, int>("env", "fs_readDir", fs_readdir);
        _linker.DefineFunction<int>("env", "fs_rewindDir", fs_rewinddir);
        _linker.DefineFunction<int>("env", "fs_closeDir", fs_closedir);
        _linker.DefineFunction<int, int, int, int, int>("env", "fs_allocMemoryCard", fs_allocMemoryCard);
        _linker.DefineFunction<int, int>("env", "bios_loadProgram", bios_loadProgram);
        _linker.DefineFunction<int>("env", "bios_getPrefLang", bios_getPrefLang);
        _linker.DefineFunction<int>("env", "bios_setPrefLang", bios_setPrefLang);
        _linker.DefineFunction("env", "bios_getPrefAudioVolume", bios_getPrefAudioVolume);
        _linker.DefineFunction<float>("env", "bios_setPrefAudioVolume", bios_setPrefAudioVolume);
        _linker.DefineFunction("env", "bios_getPrefVideoOutput", bios_getPrefVideoOutput);
        _linker.DefineFunction<int>("env", "bios_setPrefVideoOutput", bios_setPrefVideoOutput);
        _linker.DefineFunction("env", "bios_getPref24HrClock", bios_getPref24HrClock);
        _linker.DefineFunction<int>("env", "bios_setPref24HrClock", bios_setPref24HrClock);
        _linker.DefineFunction("env", "bios_savePrefs", bios_savePrefs);
        _linker.DefineFunction("env", "clock_getTimestamp", clock_getTimestamp);
        _linker.DefineFunction<long, int>("env", "clock_timestampToDatetime", clock_timestampToDatetime);
        _linker.DefineFunction<int, long>("env", "clock_datetimeToTimestamp", clock_datetimeToTimestamp);
        _linker.DefineFunction<int, int, int, int>("env", "__assert_fail", __assert_fail);
        _linker.DefineFunction<int, int, int>("env", "emscripten_memcpy_big", emscripten_memcpy_big);
        _linker.DefineFunction<int, int>("env", "emscripten_resize_heap", emscripten_resize_heap);

        _queueLoadModule = module;
    }

    public void Dispose()
    {
        foreach (var f in _files)
        {
            f?.Close();
        }
    }

    public void LoadWasmModule(Span<byte> wasmData)
    {
    #if FORCE_PRECOMPILED_RUNTIME
        throw new NotImplementedException();
    #else
        _queueLoadModule = Module.FromBytes(engine, "main", wasmData);
    #endif
    }

    public void Tick()
    {
        if (_queueLoadModule != null)
        {
            initModule(_queueLoadModule);
            _queueLoadModule = null;

            int mainRet = _main!.Invoke();
            Console.WriteLine($"main() returned {mainRet}");
        }

        _vsync?.Invoke();
    }

    public Span<T> GetSpan<T>(int address, int elemCount)
        where T : unmanaged
    {
        return memory!.GetSpan<T>(address)[..elemCount];
    }

    private void initModule(Module newModule)
    {
        module = newModule;

        _tableName = "";
        bool found = false;

        foreach (var export in module.Exports) {
            if (export is TableExport) {
                Console.WriteLine("Found function table: " + export.Name);
                _tableName = export.Name;
                found = true;
                break;
            }
        }

        if (!found) {
            Console.WriteLine("WARNING: No function table exported! Callbacks will not work correctly");
        }

        moduleInstance = _linker.Instantiate(_store, module);
        memory = moduleInstance.GetMemory("memory") ?? throw new Exception("Module is missing memory export");

        // main takes argc & argv, but we don't care about that, so just wrap it and pass (0, NULL)
        var internalMain = moduleInstance.GetFunction<int, int, int>("main") ?? throw new Exception("Module is missing main() entry point");
        _main = () =>
        {
            try
            {
                return internalMain(0, 0);
            }
            catch (WasmtimeException e)
            {
                Console.WriteLine($"RUNTIME ERROR: {e.Message}");
                _vsync = null;
                return -1;
            }
        };

        // sanity check that module doesn't ask for too much memory
        if (memory.Maximum > TOTAL_MEM)
        {
            throw new Exception("Module memory exceeds 256 page maximum");
        }

        _malloc = moduleInstance.GetFunction<int, int>("malloc") ?? throw new Exception("Module is missing malloc()");
        _free = moduleInstance.GetAction<int>("free") ?? throw new Exception("Module is missing free()");
        __errno_location = moduleInstance.GetFunction<int>("__errno_location") ?? throw new Exception("Module is missing __errno_location");

        /*
            typedef struct {
                char name[128];
                uint64_t created;
                uint64_t modified;
                uint32_t size;
                uint32_t isDirectory;
            } IODIRENT; // 152 bytes
         */
        _dirEntBuffer = _malloc((152 * 32) + 4);
        if (_dirEntBuffer % 8 != 0)
        {
            Console.WriteLine("Fixing up dirent buffer alignment (TODO: this should be fixed in DBSDK)");
            _dirEntBuffer += 4;
        }
        Console.WriteLine($"Allocated dirent buffer at: 0x{_dirEntBuffer:X8}");

        _vsync = null;
    }

    private void setErrno(DreamboxErrno err)
    {
        memory!.WriteInt32(__errno_location!(), (int)err);
    }

    private void __assert_fail(int assertionMsgPtr, int filenamePtr, int line, int fnNamePtr)
    {
        string assertionMsg = memory!.ReadNullTerminatedString(assertionMsgPtr);
        string filename = memory.ReadNullTerminatedString(filenamePtr);
        string fnName = memory.ReadNullTerminatedString(fnNamePtr);

        throw new Exception($"Assertion failed: {assertionMsg}, file {filename}, function {fnName} line {line}");
    }

    private long clock_getTimestamp()
    {
        return (long)MemCardFS.DateTimeToTimestamp(DateTime.Now);
    }

    private void clock_timestampToDatetime(long timestamp, int datetimePtr)
    {
        DateTime dt = MemCardFS.TimestampToDateTime((ulong)timestamp);
        memory!.Write(datetimePtr, new clock_dateTime
        {
            year = (ushort)dt.Year,
            month = (byte)dt.Month,
            day = (byte)dt.Day,
            hour = (byte)dt.Hour,
            minute = (byte)dt.Minute,
            second = (byte)dt.Second
        });
    }

    private long clock_datetimeToTimestamp(int datetimePtr)
    {
        clock_dateTime cdt = memory!.Read<clock_dateTime>(datetimePtr);
        DateTime dt = new DateTime(cdt.year, cdt.month, cdt.day, cdt.hour, cdt.minute, cdt.second);
        return (long)MemCardFS.DateTimeToTimestamp(dt);
    }

    private int fs_allocMemoryCard(int filenamePtr, int iconPtr, int iconPalettePtr, int blocks)
    {
        int handle = -1;

        for (int i = 0; i < _files.Length; i++)
        {
            if (_files[i] == null)
            {
                handle = i;
                break;
            }
        }

        if (handle == -1)
        {
            setErrno(DreamboxErrno.ENFILE);
            return 0;
        }

        string filename = memory!.ReadNullTerminatedString(filenamePtr);
        Span<byte> iconData = GetSpan<byte>(iconPtr, 128);
        Span<ushort> paletteData = GetSpan<ushort>(iconPalettePtr, 16);

        Stream stream;

        try
        {
            if (filename.StartsWith("/ma/"))
            {
                stream = _mca.fs.OpenCreate(filename.Substring(4), iconData.ToArray(), paletteData.ToArray(), blocks * 512);
            }
            else if (filename.StartsWith("/mb/"))
            {
                stream = _mcb.fs.OpenCreate(filename.Substring(4), iconData.ToArray(), paletteData.ToArray(), blocks * 512);
            }
            else
            {
                setErrno(DreamboxErrno.ENODEV);
                return 0;
            }
        }
        catch (IOException e)
        {
            setErrno((DreamboxErrno)e.HResult);
            return 0;
        }

        _files[handle] = stream;
        return (handle + 1);
    }

    private int fs_deviceExists(int devstrPtr)
    {
        string devstr = memory!.ReadNullTerminatedString(devstrPtr);

        if (devstr == "cd")
        {
            setErrno(DreamboxErrno.ESUCCESS);
            return _disk.Inserted() ? 1 : 0;
        }
        else if (devstr == "ma" || devstr == "mb")
        {
            // at the moment memory cards are ALWAYS connected
            setErrno(DreamboxErrno.ESUCCESS);
            return 1;
        }
        else
        {
            setErrno(DreamboxErrno.ENODEV);
            return 0;
        }
    }

    private void fs_deviceEject(int devstrPtr)
    {
        string devstr = memory!.ReadNullTerminatedString(devstrPtr);

        if (devstr == "cd")
        {
            setErrno(DreamboxErrno.ESUCCESS);
            _disk.Eject();
        }
        else if (devstr == "ma" || devstr == "mb")
        {
            setErrno(DreamboxErrno.ENOTTTY);
        }
        else
        {
            setErrno(DreamboxErrno.ENODEV);
        }
    }

    private int fs_opendir(int pathstr)
    {
        int handle = -1;

        for (int i = 0; i < _dirs.Length; i++)
        {
            if (_dirs[i] == null)
            {
                handle = i;
                break;
            }
        }

        if (handle == -1)
        {
            setErrno(DreamboxErrno.ENFILE);
            return 0;
        }

        string path = memory!.ReadNullTerminatedString(pathstr);

        DirectoryReader dirReader;
        if (path.StartsWith("/cd/"))
        {
            try
            {
                dirReader = _disk.OpenDirectory(path[4..]) ?? throw new DirectoryNotFoundException();
            }
            catch (DirectoryNotFoundException)
            {
                setErrno(DreamboxErrno.ENOENT);
                return 0;
            }
        }
        else if (path.StartsWith("/ma/"))
        {
            if (path.Length > 4)
            {
                setErrno(DreamboxErrno.ENOENT);
                return 0;
            }

            dirReader = new DirectoryReader(_mca);
        }
        else if (path.StartsWith("/mb/"))
        {
            if (path.Length > 4)
            {
                setErrno(DreamboxErrno.ENOENT);
                return 0;
            }

            dirReader = new DirectoryReader(_mcb);
        }
        else
        {
            setErrno(DreamboxErrno.ENODEV);
            return 0;
        }

        setErrno(DreamboxErrno.ESUCCESS);
        _dirs[handle] = dirReader;
        return handle + 1;
    }

    private int fs_readdir(int handle)
    {
        handle--;

        if (handle < 0 || handle >= _dirs.Length || _dirs[handle] == null)
        {
            setErrno(DreamboxErrno.EBADF);
            return 0;
        }

        if (_dirs[handle]?.ReadNext() is DirEnt d)
        {
            int ptr = _dirEntBuffer + (152 * handle);
            
            int nameLen = memory!.WriteString(ptr, d.name);
            memory.WriteByte(ptr + nameLen, 0);

            memory.WriteInt64(ptr + 128, (long)MemCardFS.DateTimeToTimestamp(d.created));
            memory.WriteInt64(ptr + 136, (long)MemCardFS.DateTimeToTimestamp(d.modified));
            memory.WriteInt32(ptr + 144, d.size);
            memory.WriteInt32(ptr + 148, d.isDirectory ? 1 : 0);
            setErrno(DreamboxErrno.ESUCCESS);
            return ptr;
        }

        setErrno(DreamboxErrno.ESUCCESS);
        return 0;
    }

    private void fs_rewinddir(int handle)
    {
        handle--;
        
        if (handle < 0 || handle >= _dirs.Length || _dirs[handle] == null)
        {
            setErrno(DreamboxErrno.EBADF);
            return;
        }

        setErrno(DreamboxErrno.ESUCCESS);
        _dirs[handle]?.Rewind();
    }

    private void fs_closedir(int handle)
    {
        handle--;

        if (handle < 0 || handle >= _dirs.Length || _dirs[handle] == null)
        {
            setErrno(DreamboxErrno.EBADF);
            return;
        }

        setErrno(DreamboxErrno.ESUCCESS);
        _dirs[handle] = null;
    }

    private int fs_open(int pathstr, int mode)
    {
        int handle = -1;

        for (int i = 0; i < _files.Length; i++)
        {
            if (_files[i] == null)
            {
                handle = i;
                break;
            }
        }

        if (handle == -1)
        {
            setErrno(DreamboxErrno.ENFILE);
            return 0;
        }

        string path = memory!.ReadNullTerminatedString(pathstr);

        Stream stream;

        if (path.StartsWith("/cd/"))
        {
            // cd only supports read access
            if (mode != 0)
            {
                setErrno(DreamboxErrno.EROFS);
                return 0;
            }

            try
            {
                stream = _disk.OpenRead(path[4..].Replace('/', '\\'));
            }
            catch(FileNotFoundException)
            {
                setErrno(DreamboxErrno.ENOENT);
                return 0;
            }
        }
        else if (path.StartsWith("/ma/"))
        {
            try
            {
                if (mode == 0)
                {
                    stream = _mca.fs.OpenRead(path[4..]);
                }
                else if (mode == 1)
                {
                    stream = _mcb.fs.OpenWrite(path[4..]);
                }
                else
                {
                    setErrno(DreamboxErrno.EINVAL);
                    return 0;
                }
            }
            catch(FileNotFoundException)
            {
                setErrno(DreamboxErrno.ENOENT);
                return 0;
            }
            catch(IOException e)
            {
                setErrno((DreamboxErrno)e.HResult);
                return 0;
            }
        }
        else if (path.StartsWith("/mb/"))
        {
            try
            {
                if (mode == 0)
                {
                    stream = _mca.fs.OpenRead(path[4..]);
                }
                else if (mode == 1)
                {
                    stream = _mcb.fs.OpenWrite(path[4..]);
                }
                else
                {
                    setErrno(DreamboxErrno.EINVAL);
                    return 0;
                }
            }
            catch (FileNotFoundException)
            {
                setErrno(DreamboxErrno.ENOENT);
                return 0;
            }
            catch (IOException e)
            {
                setErrno((DreamboxErrno)e.HResult);
                return 0;
            }
        }
        else
        {
            setErrno(DreamboxErrno.ENODEV);
            return 0;
        }

        setErrno(DreamboxErrno.ESUCCESS);
        _files[handle] = stream;
        return (handle + 1);
    }

    private int fs_fileExists(int pathstr)
    {
        string path = memory!.ReadNullTerminatedString(pathstr);

        if (path.StartsWith("/cd/"))
        {
            setErrno(DreamboxErrno.ESUCCESS);
            return _disk.FileExists(path[4..].Replace('/', '\\')) ? 1 : 0;
        }
        else if (path.StartsWith("/ma/"))
        {
            setErrno(DreamboxErrno.ESUCCESS);
            return _mca.fs.Exists(path[4..]) ? 1 : 0;
        }
        else if (path.StartsWith("/mb/"))
        {
            setErrno(DreamboxErrno.ESUCCESS);
            return _mcb.fs.Exists(path[4..]) ? 1 : 0;
        }
        else
        {
            setErrno(DreamboxErrno.ESUCCESS);
            return 0;
        }
    }

    private void fs_close(int handle)
    {
        handle--;

        if (handle < 0 || handle >= _files.Length)
        {
            setErrno(DreamboxErrno.EBADF);
            return;
        }

        setErrno(DreamboxErrno.ESUCCESS);
        _files[handle]?.Close();
        _files[handle]?.Dispose();
        _files[handle] = null;
    }

    private int fs_eof(int handle)
    {
        handle--;

        if (handle < 0 || handle >= _files.Length)
        {
            setErrno(DreamboxErrno.EBADF);
            return 0;
        }

        if (_files[handle] is Stream stream)
        {
            setErrno(DreamboxErrno.ESUCCESS);
            return (stream.Position >= stream.Length) ? 1 : 0;
        }
        
        setErrno(DreamboxErrno.EBADF);
        return 0;
    }

    private int fs_read(int handle, int buffer, int len)
    {
        handle--;

        if (handle < 0 || handle >= _files.Length)
        {
            setErrno(DreamboxErrno.EBADF);
            return 0;
        }

        Stream? fs = _files[handle];

        if (fs == null)
        {
            setErrno(DreamboxErrno.EBADF);
            return 0;
        }

        // ensure file was opened for read
        if (!fs.CanRead)
        {
            setErrno(DreamboxErrno.EACCESS);
            return 0;
        }

        setErrno(DreamboxErrno.ESUCCESS);
        Span<byte> dstBuffer = GetSpan<byte>(buffer, len);
        int result = (int)fs.Read(dstBuffer);

        return result;
    }

    private int fs_write(int handle, int buffer, int len)
    {
        handle--;

        if (handle < 0 || handle >= _files.Length)
        {
            setErrno(DreamboxErrno.EBADF);
            return 0;
        }

        Stream? fs = _files[handle];

        if (fs == null)
        {
            setErrno(DreamboxErrno.EBADF);
            return 0;
        }

        // ensure file was opened for write
        if (!fs.CanWrite)
        {
            setErrno(DreamboxErrno.EACCESS);
            return 0;
        }

        Span<byte> dstBuffer = GetSpan<byte>(buffer, len);

        try
        {
            fs.Write(dstBuffer);
        }
        catch(IOException e)
        {
            setErrno((DreamboxErrno)e.HResult);
            return 0;
        }

        setErrno(DreamboxErrno.ESUCCESS);
        return dstBuffer.Length;
    }

    private void fs_flush(int handle)
    {
        handle--;

        if (handle < 0 || handle >= _files.Length)
        {
            setErrno(DreamboxErrno.EBADF);
            return;
        }

        Stream? fs = _files[handle];

        if (fs == null)
        {
            setErrno(DreamboxErrno.EBADF);
            return;
        }

        // ensure file was opened for write
        if (!fs.CanWrite)
        {
            setErrno(DreamboxErrno.EACCESS);
            return;
        }

        try
        {
            fs.Flush();
        }
        catch (IOException e)
        {
            setErrno((DreamboxErrno)e.HResult);
        }
        
        setErrno(DreamboxErrno.ESUCCESS);
    }

    private int fs_seek(int handle, int position, int whence)
    {
        handle--;

        if (handle < 0 || handle >= _files.Length)
        {
            setErrno(DreamboxErrno.EBADF);
            return 0;
        }

        Stream? fs = _files[handle];

        if (fs == null)
        {
            setErrno(DreamboxErrno.EBADF);
            return 0;
        }

        try
        {
            setErrno(DreamboxErrno.ESUCCESS);
            return (int)fs.Seek(position, (SeekOrigin)whence);
        }
        catch (IOException e)
        {
            setErrno((DreamboxErrno)e.HResult);
            return 0;
        }
    }

    private int fs_tell(int handle)
    {
        handle--;

        if (handle < 0 || handle >= _files.Length)
        {
            setErrno(DreamboxErrno.EBADF);
            return 0;
        }

        Stream? fs = _files[handle];

        if (fs == null)
        {
            setErrno(DreamboxErrno.EBADF);
            return 0;
        }

        setErrno(DreamboxErrno.ESUCCESS);
        return (int)fs.Position;
    }

    private void mat_load(int matpatr)
    {
        _mat4 = memory!.Read<Matrix4x4>(matpatr);
    }

    private void mat_store(int matpatr)
    {
        memory!.Write<Matrix4x4>(matpatr, _mat4);
    }

    private void mat_mul(int matptr)
    {
        Matrix4x4 m = memory!.Read<Matrix4x4>(matptr);
        _mat4 *= m;
    }

    private void mat_transform(int invec_ptr, int outvec_ptr, int count, int stride)
    {
        for (int i = 0; i < count; i++)
        {
            Vector4 v = memory!.Read<Vector4>(invec_ptr);
            v = Vector4.Transform(v, _mat4);
            memory.Write<Vector4>(outvec_ptr, v);

            invec_ptr += stride;
            outvec_ptr += stride;
        }
    }

    private int gamepad_isConnected(int slot)
    {
        return _inputSystem.gamepads[slot] != null ? 1 : 0;
    }

    private void gamepad_setRumble(int slot, int enable)
    {
        _inputSystem.gamepads[slot]?.SetRumble(enable != 0);
    }

    private void gamepad_readState(int slot, int statePtr)
    {
        _inputSystem.gamepads[slot]?.PollState();
        var state = _inputSystem.gamepads[slot]?.GetState() ?? new Gamepad.State();

        memory!.Write(statePtr, state);
    }

    private void db_log(int ptr)
    {
        string msg = memory!.ReadNullTerminatedString(ptr);
        Console.WriteLine("[DBG] " + msg);

        OnLogOutput?.Invoke(msg);
    }

    private void vdp_clearColor(int colorPtr)
    {
        _vdp.ClearColor(memory!.Read<Color32>(colorPtr));
    }

    private void vdp_clearDepth(float depth)
    {
        _vdp.ClearDepth(depth);
    }

    private void vdp_clearStencil(int stencil)
    {
        throw new NotImplementedException();
    }

    private void vdp_depthWrite(int enabled)
    {
        _vdp.DepthWrite(enabled != 0);
    }

    private void vdp_depthFunc(int comparison)
    {
        _vdp.DepthFunc((VDPCompare)comparison);
    }

    private void vdp_blendEquation(int mode)
    {
        _vdp.BlendEquation((VDPBlendEquation)mode);
    }

    private void vdp_blendFunc(int srcFactor, int dstFactor)
    {
        _vdp.BlendFunc((VDPBlendFactor)srcFactor, (VDPBlendFactor)dstFactor);
    }

    private void vdp_setWinding(int windingOrder)
    {
        _vdp.SetWinding((VDPWindingOrder)windingOrder);
    }

    private void vdp_setCulling(int enabled)
    {
        _vdp.SetCulling(enabled != 0);
    }

    private void vdp_drawGeometry(int topology, int first, int count, int vertexDataPtr)
    {
        var vtxSpan = memory!.GetSpan<Vertex>(vertexDataPtr).Slice(first, count);
        int newLen = _tmpPackedVertex.Length;
        
        while (newLen < vtxSpan.Length) {
            newLen *= 2;
        }
        
        if (_tmpPackedVertex.Length < newLen) {
            _tmpPackedVertex = new PackedVertex[newLen];
            _tmpPackedVertexBufferData = new byte[newLen * Unsafe.SizeOf<PackedVertex>()];
        }
        
        for (int i = 0; i < vtxSpan.Length; i++)
        {
            var vtx = vtxSpan[i];
            _tmpPackedVertex[i] = new PackedVertex
            {
                position = vtx.position,
                texcoord = new Vector2(vtx.texcoord.X, vtx.texcoord.Y),
                color = new Color32(vtx.color),
                ocolor = new Color32(vtx.ocolor),
            };
        }
        
        unsafe
        {
            fixed (void* src = _tmpPackedVertex)
            fixed (void* dst = _tmpPackedVertexBufferData)
            {
                Unsafe.CopyBlock(dst, src, (uint)(vtxSpan.Length * Unsafe.SizeOf<PackedVertex>()));
            }
        }

        int byteCount = vtxSpan.Length * Unsafe.SizeOf<PackedVertex>();

        _vdp.SubmitVU((VDPTopology)topology, _tmpPackedVertexBufferData.AsSpan()[..byteCount]);
    }

    private void vdp_drawGeometryPacked(int topology, int first, int count, int vertexDataPtr)
    {
        var vtxSpan = memory!.GetSpan<byte>(vertexDataPtr).Slice(first, count * Unsafe.SizeOf<PackedVertex>());
        _vdp.SubmitVU((VDPTopology)topology, vtxSpan);
    }

    private void vdp_setVsyncHandler(int handlerPtr)
    {
        var table = moduleInstance!.GetTable(_tableName);
        if (table == null) return;

        try
        {
            var handler = (Function?)table.GetElement((uint)handlerPtr);
            _vsync = () =>
            {
                try
                {
                    handler?.Invoke();
                }
                catch (WasmtimeException e)
                {
                    Console.WriteLine($"RUNTIME ERROR: {e.Message}");
                    _vsync = null;
                }
            };
        }
        catch (IndexOutOfRangeException)
        {
            Console.WriteLine("vdp_setVsyncHandler: INVALID FN PTR: " + handlerPtr);
        }
    }

    private int vdp_allocTexture(int mipmap, int textureFormat, int width, int height)
    {
        return _vdp.AllocTexture(mipmap != 0, (VDPTextureFormat)textureFormat, width, height);
    }

    private int vdp_allocRenderTexture(int width, int height)
    {
        return _vdp.AllocRenderTexture(width, height);
    }

    private void vdp_releaseTexture(int handle)
    {
        _vdp.ReleaseTexture(handle);
    }

    private void vdp_copyFbToTexture(int srcRectPtr, int dstRectPtr, int dstTextureHandle)
    {
        SDL.SDL_Rect srcRect = memory!.Read<SDL.SDL_Rect>(srcRectPtr);
        SDL.SDL_Rect dstRect = memory!.Read<SDL.SDL_Rect>(dstRectPtr);
        _vdp.CopyFBToTexture(srcRect, dstRect, dstTextureHandle);
    }

    private void vdp_setRenderTarget(int handle)
    {
        _vdp.SetRenderTarget(handle);
    }

    private int vdp_getUsage()
    {
        return _vdp.TextureMemoryUsage;
    }

    private void vdp_textureData(int handle, int level, int dataPtr, int dataPtrLen)
    {
        var data = GetSpan<byte>(dataPtr, dataPtrLen);
        _vdp.SetTextureData(handle, level, null, data);
    }

    private void vdp_textureDataYUV(int handle, int yDataPtr, int yDataPtrLen, int uDataPtr, int uDataPtrLen, int vDataPtr, int vDataPtrLen)
    {
        var yData = GetSpan<byte>(yDataPtr, yDataPtrLen);
        var uData = GetSpan<byte>(uDataPtr, uDataPtrLen);
        var vData = GetSpan<byte>(vDataPtr, vDataPtrLen);
        _vdp.SetTextureDataYUV(handle, yData, uData, vData);
    }

    private void vdp_textureDataRegion(int handle, int level, int regionRectPtr, int dataPtr, int dataPtrLen)
    {
        var data = GetSpan<byte>(dataPtr, dataPtrLen);
        var rect = GetSpan<SDL.SDL_Rect>(regionRectPtr, 1);
        _vdp.SetTextureData(handle, level, regionRectPtr == 0 ? null : rect[0], data);
    }

    private void vdp_setSampleParams(int filter, int wrapU, int wrapV)
    {
        _vdp.SetSampleParams(0, (VDPFilter)filter, (VDPWrap)wrapU, (VDPWrap)wrapV);
    }

    private void vdp_bindTexture(int handle)
    {
        _vdp.BindTexture(0, handle);
    }

    private void vdp_setSampleParamsSlot(int slot, int filter, int wrapU, int wrapV)
    {
        _vdp.SetSampleParams(slot, (VDPFilter)filter, (VDPWrap)wrapU, (VDPWrap)wrapV);
    }

    private void vdp_bindTextureSlot(int slot, int handle)
    {
        _vdp.BindTexture(slot, handle);
    }

    private void vdp_setTexCombine(int texCombine, int vtxCombine)
    {
        _vdp.SetTexCombine((VDPTexCombine)texCombine, (VDPTexCombine)vtxCombine);
    }

    private void vdp_viewport(int x, int y, int width, int height)
    {
        _vdp.Viewport(x, y, width, height);
    }

    private void vdp_submitDepthQuery(float refVal, int compare, int x, int y, int width, int height)
    {
        _vdp.SubmitDepthQuery(refVal, (VDPCompare)compare, new SDL.SDL_Rect() { x = x, y = y, w = width, h = height });
    }

    private int vdp_getDepthQueryResult()
    {
        return _vdp.GetDepthQueryResult();
    }

    private void vdp_setVUCData(int offset, int vecPtr)
    {
        var data = GetSpan<Vector4>(vecPtr, 1);
        _vdp.SetVUCData(offset, data[0]);
    }

    private void vdp_setVULayout(int slot, int offset, int format)
    {
        _vdp.SetVULayout(slot, offset, (VDPVertexSlotFormat)format);
    }

    private void vdp_setVUStride(int stride)
    {
        _vdp.SetVUStride(stride);
    }

    private void vdp_uploadVUProgram(int programPtr, int programLength)
    {
        var data = GetSpan<uint>(programPtr, programLength / 4);
        _vdp.UploadVUProgram(data);
    }

    private void vdp_submitVU(int topology, int dataPtr, int dataLength)
    {
        var data = GetSpan<byte>(dataPtr, dataLength);
        _vdp.SubmitVU((VDPTopology)topology, data);
    }

    private int audio_initSynth(int dataPtr, int dataLen)
    {
        var data = GetSpan<byte>(dataPtr, dataLen);
        return _audioSystem.InitSynth(data.ToArray()) ? 1 : 0;
    }

    private int audio_playMidi(int dataPtr, int dataLen, int loop)
    {
        var data = GetSpan<byte>(dataPtr, dataLen);
        return _audioSystem.PlayMidi(data.ToArray(), loop != 0) ? 1 : 0;
    }

    private void audio_setMidiReverb(int enabled)
    {
        _audioSystem.SetMidiReverb(enabled != 0);
    }

    private void audio_setMidiVolume(float volume)
    {
        _audioSystem.SetMidiVolume(volume);
    }

    private int audio_alloc(int dataPtr, int dataLen, int audioFmt)
    {
        return _audioSystem.Allocate(GetSpan<byte>(dataPtr, dataLen), (AudioFormat)audioFmt);
    }

    private int audio_allocCompressed(int dataPtr, int dataLen, int chunkLen)
    {
        return _audioSystem.AllocateCompressed(GetSpan<byte>(dataPtr, dataLen), chunkLen);
    }

    private void audio_free(int handle)
    {
        _audioSystem.Free(handle);
    }

    private int audio_getUsage()
    {
        return _audioSystem.TotalMemUsage;
    }

    private void audio_queueSetParam_i(int slot, int param, int value, double time)
    {
        _audioSystem.QueueSetVoiceParam_i(slot, (AudioVoiceParameter)param, value, time);
    }

    private void audio_queueSetParam_f(int slot, int param, float value, double time)
    {
        _audioSystem.QueueSetVoiceParam_f(slot, (AudioVoiceParameter)param, value, time);
    }

    private void audio_queueStartVoice(int slot, double time)
    {
        _audioSystem.QueueStartVoice(slot, time);
    }

    private void audio_queueStopVoice(int slot, double time)
    {
        _audioSystem.QueueStopVoice(slot, time);
    }

    private int audio_getVoiceState(int slot)
    {
        return _audioSystem.GetVoiceState(slot) ? 1 : 0;
    }

    private double audio_getTime()
    {
        return _audioSystem.AudioTime;
    }

    private void audio_setReverbParams(float roomSize, float damp, float width, float wet, float dry)
    {
        _audioSystem.SetReverbParams(roomSize, damp, width, wet, dry);
    }

    private void bios_loadProgram(int codePtr, int codeLen)
    {
        var programData = GetSpan<byte>(codePtr, codeLen).ToArray();
        LoadWasmModule(programData);
    }

    private void bios_getPrefLang(int outLangStr)
    {
        // why on earth does this not write a null terminator
        int ct = memory!.WriteString(outLangStr, _config.Lang);
        memory.WriteByte(outLangStr + ct, 0);
    }

    private void bios_setPrefLang(int langStr)
    {
        _config.Lang = memory!.ReadNullTerminatedString(langStr);
    }

    private float bios_getPrefAudioVolume()
    {
        return _config.AudioVolume;
    }

    private void bios_setPrefAudioVolume(float volume)
    {
        if (volume < 0f) volume = 0f;
        if (volume > 1f) volume = 1f;

        _config.AudioVolume = volume;
    }

    private int bios_getPrefVideoOutput()
    {
        return (int)_config.VideoMode;
    }

    private void bios_setPrefVideoOutput(int mode)
    {
        _config.VideoMode = (DreamboxVideoMode)mode;
    }

    private int bios_getPref24HrClock()
    {
        return _config.DisplayClock24Hr ? 1 : 0;
    }

    private void bios_setPref24HrClock(int enable)
    {
        _config.DisplayClock24Hr = enable != 0;
    }

    private void bios_savePrefs()
    {
        DreamboxConfig.SavePrefs(_config);
    }

    private void emscripten_memcpy_big(int destPtr, int srcPtr, int copyLen)
    {
        var src = GetSpan<byte>(srcPtr, copyLen);
        var dst = GetSpan<byte>(destPtr, copyLen);
        src.CopyTo(dst);
    }

    private int emscripten_resize_heap(int newSize)
    {
        int newPages = newSize / Memory.PageSize;
        if (newPages > TOTAL_MEM)
        {
            throw new NotImplementedException($"Out of memory (runtime requested {newPages} pages, exceeding {TOTAL_MEM} page limit)");
        }
        return newSize;
    }
}