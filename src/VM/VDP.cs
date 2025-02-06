using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using DreamboxVM.Graphics;
using SDL3;

namespace DreamboxVM.VM;

public enum VDPCompare
{
    NEVER       = 0x0200,
    LESS        = 0x0201,
    EQUAL       = 0x0202,
    LEQUAL      = 0x0203,
    GREATER     = 0x0204,
    NOTEQUAL    = 0x0205,
    GEQUAL      = 0x0206,
    ALWAYS      = 0x0207,
}

public enum VDPBlendEquation
{
    ADD               = 0x8006,
    SUBTRACT          = 0x800A,
    REVERSE_SUBTRACT  = 0x800B,
}

public enum VDPBlendFactor
{
    ZERO                  = 0,
    ONE                   = 1,
    SRC_COLOR             = 0x0300,
    ONE_MINUS_SRC_COLOR   = 0x0301,
    SRC_ALPHA             = 0x0302,
    ONE_MINUS_SRC_ALPHA   = 0x0303,
    DST_ALPHA             = 0x0304,
    ONE_MINUS_DST_ALPHA   = 0x0305,
    DST_COLOR             = 0x0306,
    ONE_MINUS_DST_COLOR   = 0x0307,
}

public enum VDPWindingOrder
{
    CW  = 0x0900,
    CCW = 0x0901,
}

public enum VDPTopology
{
    LINE_LIST       = 0x0000,
    LINE_STRIP      = 0x0001,
    TRIANGLE_LIST   = 0x0002,
    TRIANGLE_STRIP  = 0x0003,
}

public enum VDPTextureFormat
{
    RGB565   = 0,
    RGBA4444 = 1,
    RGBA8888 = 2,
    DXT1     = 3,
    DXT3     = 4,
    YUV420   = 5,
}

public enum VDPFilter
{
    NEAREST     = 0x2600,
    LINEAR      = 0x2601,
}

public enum VDPWrap
{
    CLAMP       = 0x812F,
    REPEAT      = 0x2901,
    MIRROR      = 0x8370,
}

public struct Color32
{
    public byte r;
    public byte g;
    public byte b;
    public byte a;

    public Color32(byte r, byte g, byte b, byte a = 255)
    {
        this.r = r;
        this.g = g;
        this.b = b;
        this.a = a;
    }

    public Color32(Vector4 v)
    {
        r = (byte)(Math.Clamp(v.X, 0.0f, 1.0f) * 255.0);
        g = (byte)(Math.Clamp(v.Y, 0.0f, 1.0f) * 255.0);
        b = (byte)(Math.Clamp(v.Z, 0.0f, 1.0f) * 255.0);
        a = (byte)(Math.Clamp(v.W, 0.0f, 1.0f) * 255.0);
    }

    public SDL.SDL_FColor ToFColor()
    {
        return new SDL.SDL_FColor() { r = r / 255.0f, g = g / 255.0f, b = b / 255.0f, a = a / 255.0f };
    }
}

public struct Vertex
{
    public Vector4 position;
    public Vector4 color;
    public Vector4 ocolor;
    public Vector4 texcoord;
}

public struct PackedVertex : IVertex
{
    public Vector4 position;
    public Vector2 texcoord;
    public Color32 color;
    public Color32 ocolor;

    public static SDL.SDL_GPUVertexAttribute[] GetLayout()
    {
        return [
            new SDL.SDL_GPUVertexAttribute()
            {
                location = 0,
                buffer_slot = 0,
                format = SDL.SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT4,
                offset = 0
            },
            new SDL.SDL_GPUVertexAttribute()
            {
                location = 1,
                buffer_slot = 0,
                format = SDL.SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT2,
                offset = 16
            },
            new SDL.SDL_GPUVertexAttribute()
            {
                location = 2,
                buffer_slot = 0,
                format = SDL.SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_UBYTE4_NORM,
                offset = 24
            },
            new SDL.SDL_GPUVertexAttribute()
            {
                location = 3,
                buffer_slot = 0,
                format = SDL.SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_UBYTE4_NORM,
                offset = 28
            },
        ];
    }
}

class VDP : IDisposable
{
    public const int VERTICES_PER_FRAME = 50000; // number of vertices you can submit per frame in order to maintain 60Hz refresh rate (~3m per second)
    public const int SCREEN_WIDTH = 640;
    public const int SCREEN_HEIGHT = 480;

    public const int MAX_TEXTURE_MEM = 8388608; // 8MiB of texture memory available

    private static readonly Dictionary<VDPBlendEquation, SDL.SDL_GPUBlendOp> BLEND_OP_TO_SDL = new () {
        { VDPBlendEquation.ADD, SDL.SDL_GPUBlendOp.SDL_GPU_BLENDOP_ADD },
        { VDPBlendEquation.SUBTRACT, SDL.SDL_GPUBlendOp.SDL_GPU_BLENDOP_SUBTRACT },
        { VDPBlendEquation.REVERSE_SUBTRACT, SDL.SDL_GPUBlendOp.SDL_GPU_BLENDOP_REVERSE_SUBTRACT },
    };

    private static readonly Dictionary<VDPBlendFactor, SDL.SDL_GPUBlendFactor> BLEND_FACTOR_TO_SDL = new() {
        { VDPBlendFactor.ZERO, SDL.SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ZERO },
        { VDPBlendFactor.ONE, SDL.SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE },
        { VDPBlendFactor.SRC_COLOR, SDL.SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_SRC_COLOR },
        { VDPBlendFactor.SRC_ALPHA, SDL.SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_SRC_ALPHA },
        { VDPBlendFactor.ONE_MINUS_SRC_COLOR, SDL.SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE_MINUS_SRC_COLOR },
        { VDPBlendFactor.ONE_MINUS_SRC_ALPHA, SDL.SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE_MINUS_SRC_ALPHA },
        { VDPBlendFactor.DST_COLOR, SDL.SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_DST_COLOR },
        { VDPBlendFactor.DST_ALPHA, SDL.SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_DST_ALPHA },
        { VDPBlendFactor.ONE_MINUS_DST_COLOR, SDL.SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE_MINUS_DST_COLOR },
        { VDPBlendFactor.ONE_MINUS_DST_ALPHA, SDL.SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE_MINUS_DST_ALPHA },
    };

    private static readonly Dictionary<VDPTopology, SDL.SDL_GPUPrimitiveType> TOPOLOGY_TO_SDL = new () {
        { VDPTopology.TRIANGLE_LIST, SDL.SDL_GPUPrimitiveType.SDL_GPU_PRIMITIVETYPE_TRIANGLELIST },
        { VDPTopology.TRIANGLE_STRIP, SDL.SDL_GPUPrimitiveType.SDL_GPU_PRIMITIVETYPE_TRIANGLESTRIP },
        { VDPTopology.LINE_LIST, SDL.SDL_GPUPrimitiveType.SDL_GPU_PRIMITIVETYPE_LINELIST },
        { VDPTopology.LINE_STRIP, SDL.SDL_GPUPrimitiveType.SDL_GPU_PRIMITIVETYPE_LINESTRIP },
    };

    private static readonly Dictionary<VDPCompare, SDL.SDL_GPUCompareOp> COMPARE_TO_SDL = new () {
        { VDPCompare.ALWAYS, SDL.SDL_GPUCompareOp.SDL_GPU_COMPAREOP_ALWAYS },
        { VDPCompare.NEVER, SDL.SDL_GPUCompareOp.SDL_GPU_COMPAREOP_NEVER },
        { VDPCompare.EQUAL, SDL.SDL_GPUCompareOp.SDL_GPU_COMPAREOP_EQUAL },
        { VDPCompare.NOTEQUAL, SDL.SDL_GPUCompareOp.SDL_GPU_COMPAREOP_NOT_EQUAL },
        { VDPCompare.LESS, SDL.SDL_GPUCompareOp.SDL_GPU_COMPAREOP_LESS },
        { VDPCompare.GREATER, SDL.SDL_GPUCompareOp.SDL_GPU_COMPAREOP_GREATER },
        { VDPCompare.LEQUAL, SDL.SDL_GPUCompareOp.SDL_GPU_COMPAREOP_LESS_OR_EQUAL },
        { VDPCompare.GEQUAL, SDL.SDL_GPUCompareOp.SDL_GPU_COMPAREOP_GREATER_OR_EQUAL },
    };

    private static readonly Dictionary<VDPFilter, SDL.SDL_GPUFilter> FILTER_TO_SDL = new () {
        { VDPFilter.LINEAR, SDL.SDL_GPUFilter.SDL_GPU_FILTER_LINEAR },
        { VDPFilter.NEAREST, SDL.SDL_GPUFilter.SDL_GPU_FILTER_NEAREST }
    };

    private static readonly Dictionary<VDPWrap, SDL.SDL_GPUSamplerAddressMode> WRAP_TO_SDL = new () {
        { VDPWrap.CLAMP, SDL.SDL_GPUSamplerAddressMode.SDL_GPU_SAMPLERADDRESSMODE_CLAMP_TO_EDGE },
        { VDPWrap.REPEAT, SDL.SDL_GPUSamplerAddressMode.SDL_GPU_SAMPLERADDRESSMODE_REPEAT },
        { VDPWrap.MIRROR, SDL.SDL_GPUSamplerAddressMode.SDL_GPU_SAMPLERADDRESSMODE_MIRRORED_REPEAT },
    };

    private static readonly Dictionary<VDPTextureFormat, SDL.SDL_GPUTextureFormat> FORMAT_TO_SDL = new () {
        { VDPTextureFormat.RGB565, SDL.SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_B5G6R5_UNORM },
        { VDPTextureFormat.RGBA4444, SDL.SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_B4G4R4A4_UNORM },
        { VDPTextureFormat.RGBA8888, SDL.SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_R8G8B8A8_UNORM },
        { VDPTextureFormat.DXT1, SDL.SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_BC1_RGBA_UNORM },
        { VDPTextureFormat.DXT3, SDL.SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_BC2_RGBA_UNORM },
    };

    class VdpTexture : IDisposable
    {
        public virtual int sizeBytes => calcTextureTotalSize(format, texture.width, texture.height, texture.levels);

        public readonly VDPTextureFormat format;
        public Texture texture;

        public VdpTexture(VDPTextureFormat format, Texture texture)
        {
            this.format = format;
            this.texture = texture;
        }

        public virtual void SetData<T>(nint copyPass, nint cmdBuf, int level, SDL.SDL_Rect? rect, Span<T> data)
            where T : unmanaged
        {
            int levelW = texture.width >> level;
            int levelH = texture.height >> level;

            if (levelW < 1) levelW = 1;
            if (levelH < 1) levelH = 1;

            texture.SetData(copyPass, cmdBuf, data, level, 0, rect?.x ?? 0, rect?.y ?? 0, 0, rect?.w ?? levelW, rect?.h ?? levelH, 1, false);
        }

        public virtual void Dispose()
        {
            this.texture.Dispose();
        }
    }

    class VdpRenderTexture : VdpTexture
    {
        public override int sizeBytes => calcTextureTotalSize(format, texture.width, texture.height, texture.levels);

        public Texture depthTexture;

        public VdpRenderTexture(VDPTextureFormat format, Texture texture, Texture depthTexture) : base(format, texture)
        {
            this.depthTexture = depthTexture;
        }

        public override void Dispose()
        {
            base.Dispose();
            this.depthTexture.Dispose();
        }
    }

    struct BlitVertex : IVertex
    {
        public Vector2 position;
        public Vector2 texcoord;

        public static SDL.SDL_GPUVertexAttribute[] GetLayout()
        {
            return [
                new SDL.SDL_GPUVertexAttribute()
                {
                    location = 0,
                    buffer_slot = 0,
                    format = SDL.SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT2,
                    offset = 0
                },
                new SDL.SDL_GPUVertexAttribute()
                {
                    location = 1,
                    buffer_slot = 0,
                    format = SDL.SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT2,
                    offset = 8
                },
            ];
        }
    }

    struct SamplerSettings : IEquatable<SamplerSettings>
    {
        public VDPFilter filter;
        public VDPWrap wrapU;
        public VDPWrap wrapV;

        public bool Equals(SamplerSettings other)
        {
            return filter == other.filter &&
                wrapU == other.wrapU &&
                wrapV == other.wrapV;
        }

        public override bool Equals([NotNullWhen(true)] object? obj) => obj is SamplerSettings other && Equals(other);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(filter);
            hash.Add(wrapU);
            hash.Add(wrapV);
            return hash.ToHashCode();
        }
    }

    struct PipelineSettings : IEquatable<PipelineSettings>
    {
        public VDPTopology topology;
        public bool depthWrite;
        public VDPCompare depthFunc;
        public VDPBlendEquation blendEquation;
        public VDPBlendFactor blendFactorSrc;
        public VDPBlendFactor blendFactorDst;
        public VDPWindingOrder winding;
        public bool culling;
        public bool enableLighting;
        public VdpRenderTexture? target;

        public bool Equals(PipelineSettings other)
        {
            return topology == other.topology &&
                depthWrite == other.depthWrite &&
                depthFunc == other.depthFunc &&
                blendEquation == other.blendEquation &&
                blendFactorSrc == other.blendFactorSrc &&
                blendFactorDst == other.blendFactorDst &&
                winding == other.winding &&
                culling == other.culling &&
                enableLighting == other.enableLighting &&
                target == other.target;
        }

        public override bool Equals([NotNullWhen(true)] object? obj) => obj is PipelineSettings other && Equals(other);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(topology);
            hash.Add(depthWrite);
            hash.Add(depthFunc);
            hash.Add(blendEquation);
            hash.Add(blendFactorSrc);
            hash.Add(blendFactorDst);
            hash.Add(winding);
            hash.Add(culling);
            hash.Add(enableLighting);
            hash.Add(target);
            return hash.ToHashCode();
        }
    }

    struct FFUniform
    {
        public Matrix4x4 transform;
        public Matrix4x4 llight;
        public Matrix4x4 lcol;
    }

    struct DrawCmd
    {
        public int vtxOffset;
        public int vtxLength;
        public VDPTopology topology;
        public bool needsNewRenderPass;
        public Color32? clearColor;
        public float? clearDepth;
        public bool needsNewPso;
        public PipelineSettings psoSettings;
        public bool needsNewTexture;
        public SamplerSettings textureSettings;
        public Texture? texture;
        public FFUniform uniforms;
    }

    public int TextureMemoryUsage => _totalTexMem;

    private readonly GraphicsDevice _graphicsDevice;
    private readonly Texture _screenTarget;
    private readonly Texture _depthTarget;
    private readonly Texture _blankTexture;

    private List<PackedVertex> _frameVertexData;
    private VertexBuffer<PackedVertex> _geoBuffer;

    private nint _activeCmdBuf = 0;
    private nint _activeRenderPass = 0;
    private nint _activeCopyPass = 0;
    private SDL.SDL_GPUViewport _activeViewport;
    private SDL.SDL_Rect _activeScissor;

    private bool _needsNewRenderPass = true;
    private Color32? _clearColor = null;
    private float? _clearDepth = null;

    private Dictionary<PipelineSettings, GraphicsPipeline> _pipelineCache = new Dictionary<PipelineSettings, GraphicsPipeline>();
    private Dictionary<SamplerSettings, nint> _samplerCache = new Dictionary<SamplerSettings, nint>();

    private int _totalTexMem = 0;
    private List<VdpTexture?> _texCache = new List<VdpTexture?>();

    // current draw state
    private bool _needsNewPipeline = false;
    private bool _needsNewTexture = false;

    private byte[] _vcop_mem_data = new byte[65536];

    private Queue<DrawCmd> _drawQueue = new Queue<DrawCmd>();

    private PipelineSettings _drawSettings = new PipelineSettings() {
        topology = VDPTopology.TRIANGLE_LIST,
        depthWrite = false,
        depthFunc = VDPCompare.ALWAYS,
        blendEquation = VDPBlendEquation.ADD,
        blendFactorSrc = VDPBlendFactor.ONE,
        blendFactorDst = VDPBlendFactor.ZERO,
        winding = VDPWindingOrder.CCW,
        culling = false,
    };

    private SamplerSettings _sampleSettings = new SamplerSettings() {
        filter = VDPFilter.LINEAR,
        wrapU = VDPWrap.REPEAT,
        wrapV = VDPWrap.REPEAT,
    };

    private Texture? _activeTexture = null;

    private Shader _blit_vertex;
    private Shader _blit_fragment;
    private Shader _ff_vertex;
    private Shader _ff_vertex_lit;
    private Shader _ff_fragment;

    private GraphicsPipeline _pso_blit;
    private ComputePipeline _vcop_interpreter;

    private VertexBuffer<BlitVertex> _blitQuad;

    private nint _linearSampler;

    private GraphicsBuffer _vcop_workmem;
    private VertexBuffer<PackedVertex> _vcop_vtxbuffer;
    private GraphicsBuffer _vcop_drawcmd;

    private FFUniform _activeUniforms;

    private int _totalVerticesThisFrame = 0;

    public VDP(GraphicsDevice graphicsDevice)
    {
        _graphicsDevice = graphicsDevice;
        _screenTarget = new Texture(graphicsDevice, SDL.SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_R8G8B8A8_UNORM, SCREEN_WIDTH, SCREEN_HEIGHT, 1,
            SDL.SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_COLOR_TARGET | SDL.SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER);
        _depthTarget = new Texture(graphicsDevice, SDL.SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_D32_FLOAT, SCREEN_WIDTH, SCREEN_HEIGHT, 1, SDL.SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_DEPTH_STENCIL_TARGET);
        _activeViewport = new SDL.SDL_GPUViewport()
        {
            x = 0,
            y = 0,
            w = SCREEN_WIDTH,
            h = SCREEN_HEIGHT,
            min_depth = 0.0f,
            max_depth = 1.0f,
        };
        _activeScissor = new SDL.SDL_Rect()
        {
            x = 0,
            y = 0,
            w = SCREEN_WIDTH,
            h = SCREEN_HEIGHT,
        };

        _blankTexture = new Texture(graphicsDevice, SDL.SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_R8G8B8A8_UNORM, 1, 1, 1, SDL.SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER);

        var cmdBuf = SDL.SDL_AcquireGPUCommandBuffer(graphicsDevice.handle);
        var copyPass = SDL.SDL_BeginGPUCopyPass(cmdBuf);
        _blankTexture.SetData(copyPass, cmdBuf, [ new Color32(255, 255, 255) ], 0, 0, 0, 0, 0, 1, 1, 1, false);
        SDL.SDL_EndGPUCopyPass(copyPass);
        SDL.SDL_SubmitGPUCommandBuffer(cmdBuf);

        _frameVertexData = new List<PackedVertex>(1024 * 3);
        _activeUniforms = new FFUniform() {
            transform = Matrix4x4.Identity,
            llight = Matrix4x4.Identity,
            lcol = Matrix4x4.Identity
        };

        // load shaders
        _blit_vertex = LoadShader(_graphicsDevice, "content/shaders/blit.vert.spv", SDL.SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_VERTEX, 0, 0, 0);
        _blit_fragment = LoadShader(_graphicsDevice, "content/shaders/blit.frag.spv", SDL.SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_FRAGMENT, 1, 0, 0);
        _ff_vertex = LoadShader(_graphicsDevice, "content/shaders/fixedfunction.vert.spv", SDL.SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_VERTEX, 0, 0, 1);
        _ff_vertex_lit = LoadShader(_graphicsDevice, "content/shaders/fixedfunction_lit.vert.spv", SDL.SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_VERTEX, 0, 0, 1);
        _ff_fragment = LoadShader(_graphicsDevice, "content/shaders/fixedfunction.frag.spv", SDL.SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_FRAGMENT, 1, 0, 0);

        // create graphics pipelines
        _pso_blit = GraphicsPipeline.Create<BlitVertex>(graphicsDevice,
            [new () {
                format = graphicsDevice.swapchainFormat,
                blend_state = new ()
                {
                    src_color_blendfactor = SDL.SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE,
                    src_alpha_blendfactor = SDL.SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE,
                    dst_color_blendfactor = SDL.SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ZERO,
                    dst_alpha_blendfactor = SDL.SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ZERO,
                    color_blend_op = SDL.SDL_GPUBlendOp.SDL_GPU_BLENDOP_ADD,
                    alpha_blend_op = SDL.SDL_GPUBlendOp.SDL_GPU_BLENDOP_ADD,
                    enable_blend = false,
                    enable_color_write_mask = false,
                }
            }], SDL.SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_INVALID, false, _blit_vertex, _blit_fragment,
            SDL.SDL_GPUPrimitiveType.SDL_GPU_PRIMITIVETYPE_TRIANGLELIST,
            new () {
                fill_mode = SDL.SDL_GPUFillMode.SDL_GPU_FILLMODE_FILL,
                cull_mode = SDL.SDL_GPUCullMode.SDL_GPU_CULLMODE_NONE,
                front_face = SDL.SDL_GPUFrontFace.SDL_GPU_FRONTFACE_CLOCKWISE,
            },
            new () {
                sample_count = SDL.SDL_GPUSampleCount.SDL_GPU_SAMPLECOUNT_1,
            },
            new () {
                compare_op = SDL.SDL_GPUCompareOp.SDL_GPU_COMPAREOP_ALWAYS
            });

        // compute pipelines
        _vcop_interpreter = new ComputePipeline(graphicsDevice, File.ReadAllBytes("content/shaders/vcop.spv"), "main", 0, 0, 0, 0, 3, 0, 1, 1, 1);

        // vertex buffers
        _blitQuad = new VertexBuffer<BlitVertex>(graphicsDevice, 6);
        SDL.SDL_SetGPUBufferName(_graphicsDevice.handle, _blitQuad.handle, nameof(_blitQuad));

        _geoBuffer = new VertexBuffer<PackedVertex>(graphicsDevice, 1024 * 3);
        SDL.SDL_SetGPUBufferName(_graphicsDevice.handle, _geoBuffer.handle, nameof(_geoBuffer));
        
        // 64KiB available to VCOP
        _vcop_workmem = new GraphicsBuffer(graphicsDevice, 65536,
            SDL.SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_COMPUTE_STORAGE_READ | SDL.SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_COMPUTE_STORAGE_WRITE);
        SDL.SDL_SetGPUBufferName(_graphicsDevice.handle, _vcop_workmem.handle, nameof(_vcop_workmem));

        // 64KiB, same as work mem (note: vertex is 32 bytes)
        _vcop_vtxbuffer = new VertexBuffer<PackedVertex>(graphicsDevice, 2048);
        SDL.SDL_SetGPUBufferName(_graphicsDevice.handle, _vcop_vtxbuffer.handle, nameof(_vcop_vtxbuffer));

        // 16 bytes to fit an indirect call struct
        _vcop_drawcmd = new GraphicsBuffer(graphicsDevice, 16,
            SDL.SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_COMPUTE_STORAGE_READ | SDL.SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_COMPUTE_STORAGE_WRITE | SDL.SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_INDIRECT);
        SDL.SDL_SetGPUBufferName(_graphicsDevice.handle, _vcop_drawcmd.handle, nameof(_vcop_drawcmd));

        // samplers
        _linearSampler = SDL.SDL_CreateGPUSampler(graphicsDevice.handle, new SDL.SDL_GPUSamplerCreateInfo() {
            min_filter = SDL.SDL_GPUFilter.SDL_GPU_FILTER_LINEAR,
            mag_filter = SDL.SDL_GPUFilter.SDL_GPU_FILTER_LINEAR,
            address_mode_u = SDL.SDL_GPUSamplerAddressMode.SDL_GPU_SAMPLERADDRESSMODE_CLAMP_TO_EDGE,
            address_mode_v = SDL.SDL_GPUSamplerAddressMode.SDL_GPU_SAMPLERADDRESSMODE_CLAMP_TO_EDGE,
        });
    }

    private Shader LoadShader(GraphicsDevice device, string path, SDL.SDL_GPUShaderStage stage,
        int numSamplers, int numStorageBuffers, int numUniformBuffers)
    {
        Console.WriteLine("Loading shader: " + path);
        byte[] data = File.ReadAllBytes(path);
        return new Shader(device, data, "main", stage, numSamplers, numStorageBuffers, numUniformBuffers);
    }

    private void FlushDrawQueue()
    {
        if (_geoBuffer.vertexCount < _frameVertexData.Count)
        {
            _geoBuffer.Dispose();
            _geoBuffer = new VertexBuffer<PackedVertex>(_graphicsDevice, _frameVertexData.Capacity);
        }

        var copyPass = SDL.SDL_BeginGPUCopyPass(_activeCmdBuf);
        _geoBuffer.SetData(copyPass, CollectionsMarshal.AsSpan(_frameVertexData), 0, true);
        SDL.SDL_EndGPUCopyPass(copyPass);

        while (_drawQueue.TryDequeue(out var cmd))
        {
            // note: lighting enable doubles the cost of drawing
            if (cmd.psoSettings.enableLighting) {
                _totalVerticesThisFrame += cmd.vtxLength * 2;
            }
            else {
                _totalVerticesThisFrame += cmd.vtxLength;
            }

            if (cmd.needsNewRenderPass)
            {
                FlushRenderPass();
                _clearColor = cmd.clearColor;
                _clearDepth = cmd.clearDepth;
            }
            
            CheckRenderPass();

            SDL.SDL_BindGPUVertexBuffers(_activeRenderPass, 0, [
                new SDL.SDL_GPUBufferBinding() {
                    buffer = _geoBuffer.handle,
                    offset = 0
                }
            ], 1);

            if (cmd.needsNewPso)
            {
                var pipelineSettings = cmd.psoSettings;
                pipelineSettings.topology = cmd.topology;

                var pso = GetOrCreatePipeline(pipelineSettings);
                SDL.SDL_BindGPUGraphicsPipeline(_activeRenderPass, pso.handle);
            }

            if (cmd.needsNewTexture)
            {
                SDL.SDL_BindGPUFragmentSamplers(_activeRenderPass, 0, [
                    new SDL.SDL_GPUTextureSamplerBinding() {
                        texture = cmd.texture?.handle ?? _blankTexture.handle,
                        sampler = GetOrCreateSampler(cmd.textureSettings)
                    }
                ], 1);
            }

            unsafe
            {
                FFUniform* ptr = &cmd.uniforms;
                SDL.SDL_PushGPUVertexUniformData(_activeCmdBuf, 0, (nint)ptr, (uint)Unsafe.SizeOf<FFUniform>());
            }

            SDL.SDL_DrawGPUPrimitives(_activeRenderPass, (uint)cmd.vtxLength, 1, (uint)cmd.vtxOffset, 0);
        }
    }

    private void FlushCopyPass()
    {
        if (_activeCopyPass != 0)
        {
            SDL.SDL_EndGPUCopyPass(_activeCopyPass);
            _activeCopyPass = 0;
        }
    }

    private void FlushRenderPass()
    {
        if (_activeRenderPass != 0)
        {
            SDL.SDL_EndGPURenderPass(_activeRenderPass);
            _activeRenderPass = 0;
        }
        
        _needsNewRenderPass = true;
    }

    private void CheckCopyPass()
    {
        if (_drawQueue.Count > 0)
        {
            FlushDrawQueue();
        }

        FlushRenderPass();

        if (_activeCopyPass == 0)
        {
            _activeCopyPass = SDL.SDL_BeginGPUCopyPass(_activeCmdBuf);
        }
    }

    private void CheckRenderPass()
    {
        // check if we need to end current copy pass
        FlushCopyPass();

        if (_activeRenderPass == 0 || _needsNewRenderPass)
        {
            FlushRenderPass();

            // start new render pass
            _activeRenderPass = SDL.SDL_BeginGPURenderPass(_activeCmdBuf, [
                new SDL.SDL_GPUColorTargetInfo() {
                    texture = _drawSettings.target?.texture.handle ?? _screenTarget.handle,
                    clear_color = _clearColor?.ToFColor() ?? new SDL.SDL_FColor(),
                    load_op = _clearColor.HasValue ? SDL.SDL_GPULoadOp.SDL_GPU_LOADOP_CLEAR : SDL.SDL_GPULoadOp.SDL_GPU_LOADOP_LOAD,
                    store_op = SDL.SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
                }
            ], 1, new SDL.SDL_GPUDepthStencilTargetInfo() {
                texture = _drawSettings.target?.depthTexture.handle ?? _depthTarget.handle,
                clear_depth = _clearDepth ?? 0.0f,
                load_op = _clearDepth.HasValue ? SDL.SDL_GPULoadOp.SDL_GPU_LOADOP_CLEAR : SDL.SDL_GPULoadOp.SDL_GPU_LOADOP_LOAD,
                store_op = SDL.SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
                stencil_load_op = SDL.SDL_GPULoadOp.SDL_GPU_LOADOP_DONT_CARE,
                stencil_store_op = SDL.SDL_GPUStoreOp.SDL_GPU_STOREOP_DONT_CARE,
            });

            if (_activeRenderPass == 0) {
                throw new InvalidOperationException("Failed to begin render pass: " + SDL.SDL_GetError());
            }

            // set viewport & scissor rect
            SDL.SDL_SetGPUViewport(_activeRenderPass, _activeViewport);
            SDL.SDL_SetGPUScissor(_activeRenderPass, _activeScissor);

            _clearColor = null;
            _clearDepth = null;

            _needsNewRenderPass = false;

            _needsNewPipeline = true;
            _needsNewTexture = true;
        }
    }

    private nint GetOrCreateSampler(SamplerSettings settings)
    {
        if (_samplerCache.TryGetValue(settings, out var sampler))
        {
            return sampler;
        }

        // create new sampler
        sampler = SDL.SDL_CreateGPUSampler(_graphicsDevice.handle, new SDL.SDL_GPUSamplerCreateInfo()
        {
            min_filter = FILTER_TO_SDL[settings.filter],
            mag_filter = FILTER_TO_SDL[settings.filter],
            address_mode_u = WRAP_TO_SDL[settings.wrapU],
            address_mode_v = WRAP_TO_SDL[settings.wrapV]
        });

        _samplerCache.Add(settings, sampler);
        return sampler;
    }

    private GraphicsPipeline GetOrCreatePipeline(PipelineSettings settings)
    {
        if (_pipelineCache.TryGetValue(settings, out var pso))
        {
            return pso;
        }

        // create new pipeline
        pso = GraphicsPipeline.Create<PackedVertex>(_graphicsDevice,
            [new () {
                format = settings.target?.texture.format ?? _screenTarget.format,
                blend_state = new ()
                {
                    src_color_blendfactor = BLEND_FACTOR_TO_SDL[settings.blendFactorSrc],
                    src_alpha_blendfactor = BLEND_FACTOR_TO_SDL[settings.blendFactorSrc],
                    dst_color_blendfactor = BLEND_FACTOR_TO_SDL[settings.blendFactorDst],
                    dst_alpha_blendfactor = BLEND_FACTOR_TO_SDL[settings.blendFactorDst],
                    color_blend_op = BLEND_OP_TO_SDL[settings.blendEquation],
                    alpha_blend_op = BLEND_OP_TO_SDL[settings.blendEquation],
                    enable_blend = true,
                    enable_color_write_mask = false,
                }
            }], settings.target?.depthTexture.format ?? _depthTarget.format, true, settings.enableLighting ? _ff_vertex_lit : _ff_vertex, _ff_fragment,
            TOPOLOGY_TO_SDL[settings.topology],
            new () {
                fill_mode = SDL.SDL_GPUFillMode.SDL_GPU_FILLMODE_FILL,
                cull_mode = settings.culling ? SDL.SDL_GPUCullMode.SDL_GPU_CULLMODE_BACK : SDL.SDL_GPUCullMode.SDL_GPU_CULLMODE_NONE,
                front_face = settings.winding == VDPWindingOrder.CW ? SDL.SDL_GPUFrontFace.SDL_GPU_FRONTFACE_CLOCKWISE : SDL.SDL_GPUFrontFace.SDL_GPU_FRONTFACE_COUNTER_CLOCKWISE,
            },
            new () {
                sample_count = SDL.SDL_GPUSampleCount.SDL_GPU_SAMPLECOUNT_1,
            },
            new () {
                compare_op = COMPARE_TO_SDL[settings.depthFunc],
                enable_depth_write = settings.depthWrite,
                enable_depth_test = true,
            });

        _pipelineCache.Add(settings, pso);
        return pso;
    }

    private void SetBlitQuad(VertexBuffer<BlitVertex> vtxBuffer, float widthScale, float heightScale)
    {
        nint cmdBuf = SDL.SDL_AcquireGPUCommandBuffer(_graphicsDevice.handle);
        nint copyPass = SDL.SDL_BeginGPUCopyPass(cmdBuf);

        vtxBuffer.SetData<BlitVertex>(copyPass, [
            new () {
                position = new Vector2(-widthScale, -heightScale),
                texcoord = new Vector2(0.0f, 1.0f)
            },
            new () {
                position = new Vector2(widthScale, -heightScale),
                texcoord = new Vector2(1.0f, 1.0f)
            },
            new () {
                position = new Vector2(-widthScale, heightScale),
                texcoord = new Vector2(0.0f, 0.0f)
            },

            new () {
                position = new Vector2(widthScale, -heightScale),
                texcoord = new Vector2(1.0f, 1.0f)
            },
            new () {
                position = new Vector2(widthScale, heightScale),
                texcoord = new Vector2(1.0f, 0.0f)
            },
            new () {
                position = new Vector2(-widthScale, heightScale),
                texcoord = new Vector2(0.0f, 0.0f)
            },
        ], 0, true);

        SDL.SDL_EndGPUCopyPass(copyPass);
        SDL.SDL_SubmitGPUCommandBuffer(cmdBuf);
    }

    public void BeginFrame(nint cmdBuf)
    {
        _activeCmdBuf = cmdBuf;
        _activeRenderPass = 0;
        _needsNewPipeline = true;
        _needsNewRenderPass = true;
        _needsNewTexture = true;
        _frameVertexData.Clear();
        _totalVerticesThisFrame = 0;
    }

    public void ClearColor(Color32 color)
    {
        _needsNewRenderPass = true;
        _clearColor = color;
    }

    public void ClearDepth(float depth)
    {
        _needsNewRenderPass = true;
        _clearDepth = depth;
    }

    public void DepthWrite(bool enable)
    {
        _needsNewPipeline = true;
        _drawSettings.depthWrite = enable;
    }

    public void DepthFunc(VDPCompare func)
    {
        _needsNewPipeline = true;
        _drawSettings.depthFunc = func;
    }

    public void BlendEquation(VDPBlendEquation equation)
    {
        _needsNewPipeline = true;
        _drawSettings.blendEquation = equation;
    }

    public void BlendFunc(VDPBlendFactor src, VDPBlendFactor dst)
    {
        _needsNewPipeline = true;
        _drawSettings.blendFactorSrc = src;
        _drawSettings.blendFactorDst = dst;
    }

    public void SetWinding(VDPWindingOrder winding)
    {
        _needsNewPipeline = true;
        _drawSettings.winding = winding;
    }

    public void SetCulling(bool enabled)
    {
        _needsNewPipeline = true;
        _drawSettings.culling = enabled;
    }

    public void SetLighting(bool enabled)
    {
        _needsNewPipeline = true;
        _drawSettings.enableLighting = enabled;
    }

    public void SetSampleParams(VDPFilter filter, VDPWrap wrapU, VDPWrap wrapV)
    {
        _needsNewTexture = true;
        _sampleSettings.filter = filter;
        _sampleSettings.wrapU = wrapU;
        _sampleSettings.wrapV = wrapV;
    }

    public int AllocTexture(bool mipmap, VDPTextureFormat format, int width, int height)
    {
        VdpTexture vdpTex;

        if (format == VDPTextureFormat.YUV420)
        {
            // TODO
            throw new NotImplementedException();
        }
        else
        {
            // width and height must be power of two
            if ((width & (width - 1)) != 0 || (height & (height - 1)) != 0)
            {
                Console.WriteLine("Non-power-of-two texture unsupported");
                return -1;
            }

            int levelcount = mipmap ? getTotalLevelCount(width, height) : 1;
            int totalSize = calcTextureTotalSize(format, width, height, levelcount);

            if (_totalTexMem + totalSize >= MAX_TEXTURE_MEM)
            {
                Console.WriteLine("Texture memory exhausted");
                return -1;
            }

            SDL.SDL_GPUTextureUsageFlags flags = SDL.SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER;

            if (format == VDPTextureFormat.RGBA8888) {
                flags |= SDL.SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_COLOR_TARGET;
            }

            Texture texture = new Texture(_graphicsDevice, FORMAT_TO_SDL[format], width, height, levelcount, flags);
            vdpTex = new VdpTexture(format, texture);

            _totalTexMem += totalSize;
        }

        int texHandle = -1;

        for (int i = 0; i < _texCache.Count; i++)
        {
            if (_texCache[i] == null)
            {
                texHandle = i;
                _texCache[i] = vdpTex;
                break;
            }
        }

        if (texHandle == -1)
        {
            texHandle = _texCache.Count;
            _texCache.Add(vdpTex);
        }

        return texHandle;
    }

    public int AllocRenderTexture(int width, int height)
    {
        VdpTexture vdpTex;

        // width and height must be power of two
        if ((width & (width - 1)) != 0 || (height & (height - 1)) != 0)
        {
            Console.WriteLine("Non-power-of-two texture unsupported");
            return -1;
        }

        int totalSize = calcTextureTotalSize(VDPTextureFormat.RGBA8888, width, height, 1) + calcDepthTextureTotalSize(width, height, 1);

        if (_totalTexMem + totalSize >= MAX_TEXTURE_MEM)
        {
            Console.WriteLine("Texture memory exhausted");
            return -1;
        }

        Texture texture = new Texture(_graphicsDevice, FORMAT_TO_SDL[VDPTextureFormat.RGBA8888], width, height, 1, SDL.SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER | SDL.SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_COLOR_TARGET);
        Texture depthTexture = new Texture(_graphicsDevice, SDL.SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_D32_FLOAT, width, height, 1, SDL.SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_DEPTH_STENCIL_TARGET);

        vdpTex = new VdpRenderTexture(VDPTextureFormat.RGBA8888, texture, depthTexture);

        _totalTexMem += totalSize;

        int texHandle = -1;

        for (int i = 0; i < _texCache.Count; i++)
        {
            if (_texCache[i] == null)
            {
                texHandle = i;
                _texCache[i] = vdpTex;
                break;
            }
        }

        if (texHandle == -1)
        {
            texHandle = _texCache.Count;
            _texCache.Add(vdpTex);
        }

        return texHandle;
    }

    public void ReleaseTexture(int handle)
    {
        if (handle < 0 || handle >= _texCache.Count)
        {
            Console.WriteLine("Attempted to release invalid texture handle: " + handle);
            return;
        }

        _texCache[handle]?.Dispose();
        _texCache[handle] = null;
    }

    public void CopyFBToTexture(SDL.SDL_Rect srcRect, SDL.SDL_Rect dstRect, int handle)
    {
        if (handle < 0 || handle >= _texCache.Count)
        {
            Console.WriteLine("Attempted to copy framebuffer to invalid texture handle: " + handle);
            return;
        }

        var target = _texCache[handle];

        if (target == null)
        {
            Console.WriteLine("Attempted to copy framebuffer to invalid texture handle: " + handle);
            return;
        }

        FlushRenderPass();
        SDL.SDL_BlitGPUTexture(_activeCmdBuf, new SDL.SDL_GPUBlitInfo() {
            source = new SDL.SDL_GPUBlitRegion() {
                texture = _screenTarget.handle,
                x = (uint)srcRect.x,
                y = (uint)srcRect.y,
                w = (uint)srcRect.w,
                h = (uint)srcRect.h
            },
            destination = new SDL.SDL_GPUBlitRegion() {
                texture = target.texture.handle,
                x = (uint)srcRect.x,
                y = (uint)srcRect.y,
                w = (uint)srcRect.w,
                h = (uint)srcRect.h
            },
            load_op = SDL.SDL_GPULoadOp.SDL_GPU_LOADOP_DONT_CARE,
            cycle = false,
            filter = SDL.SDL_GPUFilter.SDL_GPU_FILTER_LINEAR,
        });
    }

    public void SetTextureData(int handle, int level, SDL.SDL_Rect? region, Span<byte> data)
    {
        if (handle < 0 || handle >= _texCache.Count)
        {
            Console.WriteLine("Attempted to upload data to invalid texture handle: " + handle);
            return;
        }

        // check if we need to end current render pass & start new copy pass
        CheckCopyPass();
        _texCache[handle]?.SetData(_activeCopyPass, _activeCmdBuf, level, region, data);
    }

    public void SetTextureDataYUV(int handle, Span<byte> yData, Span<byte> uData, Span<byte> vData)
    {
        throw new NotImplementedException();
    }

    // NOTE: currently depth query is not available in SDL3 GPU and is not planned as far as I'm aware
    // Skipping it for now, eventually I may see if I can implement it with a compute pass if people actually need it
    public void SubmitDepthQuery(float refVal, VDPCompare compare, SDL.SDL_Rect rect)
    {
        throw new NotImplementedException();
    }

    public int GetDepthQueryResult()
    {
        throw new NotImplementedException();
    }

    public void SetRenderTarget(int handle)
    {
        _needsNewRenderPass = true;

        if (handle == -1)
        {
            _drawSettings.target = null;
            return;
        }

        if (handle < 0 || handle >= _texCache.Count)
        {
            Console.WriteLine("Attempted to set render target to invalid texture handle: " + handle);
            return;
        }

        if (_texCache[handle] is VdpRenderTexture target)
        {
            _drawSettings.target = target;
        }
        else
        {
            Console.WriteLine("Attempted to set render target to invalid texture handle: " + handle);
        }
    }

    public void BindTexture(int handle)
    {
        _needsNewTexture = true;

        if (handle == -1)
        {
            _activeTexture = null;
            return;
        }

        if (handle < 0 || handle >= _texCache.Count)
        {
            Console.WriteLine("Attempted to bind invalid texture handle: " + handle);
            return;
        }

        _activeTexture = _texCache[handle]?.texture;
    }

    public void Viewport(int x, int y, int w, int h)
    {
        _activeViewport = new SDL.SDL_GPUViewport()
        {
            x = x,
            y = y,
            w = w,
            h = h,
            min_depth = 0.0f,
            max_depth = 1.0f,
        };

        _activeScissor = new SDL.SDL_Rect()
        {
            x = x,
            y = y,
            w = w,
            h = h
        };

        SDL.SDL_SetGPUViewport(_activeRenderPass, _activeViewport);
        SDL.SDL_SetGPUScissor(_activeRenderPass, _activeScissor);
    }

    public void LoadTransform(Matrix4x4 transform)
    {
        _activeUniforms.transform = transform;
    }

    public void LoadLightTransforms(Matrix4x4 llight, Matrix4x4 lcol)
    {
        _activeUniforms.llight = llight;
        _activeUniforms.lcol = lcol;
    }

    public void DrawGeometry(VDPTopology topology, Span<PackedVertex> vertexData)
    {
        int bufferOffset = _frameVertexData.Count;
        int bufferLen = vertexData.Length;

        _frameVertexData.AddRange(vertexData);

        // queue draw command
        _drawQueue.Enqueue(new DrawCmd() {
            vtxOffset = bufferOffset,
            vtxLength = bufferLen,
            topology = topology,
            needsNewRenderPass = _needsNewRenderPass,
            clearColor = _clearColor,
            clearDepth = _clearDepth,
            needsNewPso = _needsNewPipeline,
            psoSettings = _drawSettings,
            needsNewTexture = _needsNewTexture,
            textureSettings = _sampleSettings,
            texture = _activeTexture,
            uniforms = _activeUniforms,
        });

        _needsNewPipeline = false;
        _needsNewTexture = false;
        _needsNewRenderPass = false;
        _clearColor = null;
        _clearDepth = null;
    }

    // TODO: VCOP feature needs some SERIOUS reworking to get acceptable perf

    /*public void UploadVCOPMemory<T>(int offset, Span<T> data)
        where T : unmanaged
    {
        unsafe
        {
            fixed (byte* dstPtr = &_vcop_mem_data[offset])
            fixed (T* srcPtr = data)
            {
                Unsafe.CopyBlock(dstPtr, srcPtr, (uint)(Unsafe.SizeOf<T>() * data.Length));
            }
        }
    }

    public void InvokeVCOP(VDPTopology topology)
    {
        CheckCopyPass();
        _vcop_workmem.SetData(_activeCopyPass, _vcop_mem_data.AsSpan(), 0, true);
        FlushCopyPass();

        // begin a new compute pass
        nint computePass = SDL.SDL_BeginGPUComputePass(_activeCmdBuf, [], 0, [
            new SDL.SDL_GPUStorageBufferReadWriteBinding()
            {
                buffer = _vcop_workmem.handle,
                cycle = false
            },
            new SDL.SDL_GPUStorageBufferReadWriteBinding()
            {
                buffer = _vcop_drawcmd.handle,
                cycle = true
            },
            new SDL.SDL_GPUStorageBufferReadWriteBinding()
            {
                buffer = _vcop_vtxbuffer.handle,
                cycle = true
            }
        ], 3);

        SDL.SDL_BindGPUComputePipeline(computePass, _vcop_interpreter.handle);
        SDL.SDL_DispatchGPUCompute(computePass, 1, 1, 1);
        SDL.SDL_EndGPUComputePass(computePass);

        // submit indirect draw
        CheckRenderPass();

        if (_needsNewPipeline)
        {
            var pipelineSettings = _drawSettings;
            pipelineSettings.topology = topology;

            var pso = GetOrCreatePipeline(pipelineSettings);
            SDL.SDL_BindGPUGraphicsPipeline(_activeRenderPass, pso.handle);

            _needsNewPipeline = false;
        }

        if (_needsNewTexture)
        {
            SDL.SDL_BindGPUFragmentSamplers(_activeRenderPass, 0, [
                new SDL.SDL_GPUTextureSamplerBinding() {
                    texture = _activeTexture?.handle ?? _blankTexture.handle,
                    sampler = GetOrCreateSampler(_sampleSettings)
                }
            ], 1);

            _needsNewTexture = false;
        }

        SDL.SDL_BindGPUVertexBuffers(_activeRenderPass, 0, [
            new SDL.SDL_GPUBufferBinding() {
                buffer = _vcop_vtxbuffer.handle,
                offset = 0
            }
        ], 1);

        SDL.SDL_DrawGPUPrimitivesIndirect(_activeRenderPass, _vcop_drawcmd.handle, 0, 1);
    }*/

    public void EndFrame(out int numSkipFrames)
    {
        FlushDrawQueue();
        FlushRenderPass();

        numSkipFrames = _totalVerticesThisFrame / VERTICES_PER_FRAME;
    }

    public void BlitToScreen(nint renderPass, float widthScale, float heightScale)
    {
        // set up blit quad
        SetBlitQuad(_blitQuad, widthScale, heightScale);

        // set up render state
        SDL.SDL_BindGPUGraphicsPipeline(renderPass, _pso_blit.handle);
        SDL.SDL_BindGPUVertexBuffers(renderPass, 0, [new () {
            buffer = _blitQuad.handle,
            offset = 0
        }], 1);
        SDL.SDL_BindGPUFragmentSamplers(renderPass, 0, [
            new () {
                texture = _screenTarget.handle,
                sampler = _linearSampler
            }
        ], 1);

        // draw quad
        SDL.SDL_DrawGPUPrimitives(renderPass, 6, 1, 0, 0);
    }
    
    public void Dispose()
    {
        foreach (var tex in _texCache)
        {
            tex?.Dispose();
        }
        _texCache.Clear();

        SDL.SDL_ReleaseGPUSampler(_graphicsDevice.handle, _linearSampler);

        foreach (var kvp in _samplerCache)
        {
            SDL.SDL_ReleaseGPUSampler(_graphicsDevice.handle, kvp.Value);
        }
        _samplerCache.Clear();

        foreach (var kvp in _pipelineCache)
        {
            kvp.Value.Dispose();
        }
        _pipelineCache.Clear();

        _blitQuad.Dispose();

        _geoBuffer.Dispose();
        _vcop_workmem.Dispose();
        _vcop_vtxbuffer.Dispose();
        _vcop_drawcmd.Dispose();

        _vcop_interpreter.Dispose();
        _pso_blit.Dispose();
        _screenTarget.Dispose();
        _depthTarget.Dispose();
        _blankTexture.Dispose();

        _blit_vertex.Dispose();
        _blit_fragment.Dispose();
        _ff_vertex.Dispose();
        _ff_fragment.Dispose();
        _ff_vertex_lit.Dispose();
    }
    
    private static int getTotalLevelCount(int width, int height)
    {
        int levels = 1;
        for (
            int size = Math.Max(width, height);
            size > 1;
            levels += 1
        )
        {
            size /= 2;
        }
        return levels;
    }

    private static int calcTextureTotalSize(VDPTextureFormat format, int width, int height, int levelCount)
    {
        int totalSize = 0;

        for (int i = 0; i < levelCount; i++)
        {
            totalSize += calcTextureLevelSize(format, width, height);
            width >>= 1;
            height >>= 1;
        }

        return totalSize;
    }

    private static int calcTextureLevelSize(VDPTextureFormat format, int width, int height)
    {
        switch (format) {
            case VDPTextureFormat.RGB565:
            case VDPTextureFormat.RGBA4444:
                return width * height * 2;
            case VDPTextureFormat.RGBA8888:
                return width * height * 4;
            case VDPTextureFormat.DXT1:
                {
                    // DXT1 encodes each 4x4 block of input pixels into 64 bits of output
                    int blockCount = (width * height) / 16;
                    return blockCount * 8;
                }
            case VDPTextureFormat.DXT3:
                {
                    // DXT3 encodes each 4x4 block of input pixels into 128 bits of output
                    int blockCount = (width * height) / 16;
                    return blockCount * 16;
                }
            case VDPTextureFormat.YUV420:
                {
                    // planar format - one full-size luma plane, two half-size chroma planes
                    // one byte per pixel per plane
                    int fullsize = width * height;
                    int halfsize = (width / 2) * (height / 2);

                    return fullsize + (halfsize * 2);        
                }
        }

        throw new NotImplementedException();
    }

    private static int calcDepthTextureTotalSize(int width, int height, int levelCount)
    {
        int totalSize = 0;

        for (int i = 0; i < levelCount; i++)
        {
            totalSize += calcDepthTextureLevelSize(width, height);
            width >>= 1;
            height >>= 1;
        }

        return totalSize;
    }

    private static int calcDepthTextureLevelSize(int width, int height)
    {
        return width * height * 4;
    }
}