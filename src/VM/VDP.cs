using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
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

public enum VDPVertexSlotFormat
{
    Float1,
    Float2,
    Float3,
    Float4,
    UNorm4,
    SNorm4,
}

public enum VDPTexCombine
{
    None,
    Mul,
    Add,
    Sub,
    Mix,
    Dot3,
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

public struct VDPVertexSlot
{
    public int offset;
    public VDPVertexSlotFormat format;
}

[InlineArray(VDP.NUM_VU_VERTEX_SLOTS)]
public struct VDPVertexLayout : IEquatable<VDPVertexLayout>
{
    public VDPVertexSlot slot0;

    public readonly bool Equals(VDPVertexLayout other)
    {
        for (int i = 0; i < 8; i++)
        {
            if (this[i].offset != other[i].offset || this[i].format != other[i].format)
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals([NotNullWhen(true)] object? obj) => obj is VDPVertexLayout other && Equals(other);

    public override readonly int GetHashCode()
    {
        var hash = new HashCode();

        for (int i = 0; i < 8; i++)
        {
            hash.Add(this[i].offset);
            hash.Add(this[i].format);
        }

        return hash.ToHashCode();
    }
    
    public static bool operator ==(VDPVertexLayout left, VDPVertexLayout right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(VDPVertexLayout left, VDPVertexLayout right)
    {
        return !(left == right);
    }
}

[InlineArray(VDP.NUM_VU_CONSTANTS)]
public struct VUCData
{
    public Vector4 slot;
}

[InlineArray(VDP.MAX_VU_PROGRAM_SIZE)]
public struct VUProgram
{
    public uint instr;
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

struct VdpTextureHandle
{
    public int handle;
    public VdpTexture texture;
}

class VdpTexture : IDisposable
{
    public virtual int sizeBytes => CalcTextureTotalSize(format, texture.width, texture.height, texture.levels);

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
        if (format == VDPTextureFormat.YUV420)
        {
            Console.WriteLine("Tried to SetTextureData on YUV texture (use SetTextureDataYUV instead)");
            return;
        }

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

    public static int CalcDepthTextureTotalSize(int width, int height, int levelCount)
    {
        int totalSize = 0;

        for (int i = 0; i < levelCount; i++)
        {
            totalSize += CalcDepthTextureLevelSize(width, height);
            width >>= 1;
            height >>= 1;
        }

        return totalSize;
    }

    public static int CalcDepthTextureLevelSize(int width, int height)
    {
        return width * height * 4;
    }

    public static int GetTotalLevelCount(int width, int height)
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

    public static int CalcTextureTotalSize(VDPTextureFormat format, int width, int height, int levelCount)
    {
        int totalSize = 0;

        for (int i = 0; i < levelCount; i++)
        {
            totalSize += CalcTextureLevelSize(format, width, height);
            width >>= 1;
            height >>= 1;
        }

        return totalSize;
    }

    public static int CalcTextureLevelSize(VDPTextureFormat format, int width, int height)
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
}

class VdpRenderTexture : VdpTexture
{
    public override int sizeBytes => CalcTextureTotalSize(format, texture.width, texture.height, texture.levels);

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

class VDP : IDisposable
{
    // number of VU cycles you can spend per frame in order to maintain 60Hz refresh rate (~3m per second)
    // note: currently tuned for being able to draw 150K vertices per frame (50K triangles * 3 vertices per triangle) with a 16-instruction VU program
    public const int VU_CYCLES_PER_FRAME = 150000 * 16;

    public const int SCREEN_WIDTH = 640;
    public const int SCREEN_HEIGHT = 480;

    public const int MAX_TEXTURE_MEM = 8388608; // 8MiB of texture memory available
    public const int MAX_VU_PROGRAM_SIZE = 64;  // maximum number of instructions per VU program
    public const int NUM_VU_CONSTANTS = 16;     // number of constant slots available to VU
    public const int NUM_VU_VERTEX_SLOTS = 8;   // number of input vertex slots available to VU

    const int COLORBURST_LENGTH = 3;
    const float COLORBURST_PPS = 0.25f;
    const float COLORBURST_PPF = 0.25f;
    const float SIGNAL_NOISE = 0.01f;

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

        // bit of a special case (technically it's just an RGBA8 texture, but uses a compute shader to convert YUV)
        { VDPTextureFormat.YUV420, SDL.SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_R8G8B8A8_UNORM },
    };

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
        public VdpRenderTexture? target;
        public VDPVertexLayout vuLayout;
        public int vuStride;

        public readonly bool Equals(PipelineSettings other)
        {
            return topology == other.topology &&
                depthWrite == other.depthWrite &&
                depthFunc == other.depthFunc &&
                blendEquation == other.blendEquation &&
                blendFactorSrc == other.blendFactorSrc &&
                blendFactorDst == other.blendFactorDst &&
                winding == other.winding &&
                culling == other.culling &&
                target == other.target &&
                vuLayout == other.vuLayout &&
                vuStride == other.vuStride;
        }

        public override readonly bool Equals([NotNullWhen(true)] object? obj) => obj is PipelineSettings other && Equals(other);

        public override readonly int GetHashCode()
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
            hash.Add(target);
            hash.Add(vuLayout);
            hash.Add(vuStride);
            return hash.ToHashCode();
        }
    }

    struct FFParams
    {
        public uint texCombine;
        public uint vtxCombine;

        private uint _pad0;
        private uint _pad1;
    }

    struct ConvertYUVParams
    {
        public uint imgWidth;
        public uint imgHeight;

        private uint _pad0;
        private uint _pad1;
    }

    struct DepthQueryParams
    {
        public int rect_x;
        public int rect_y;
        public int rect_w;
        public int rect_h;
        public VDPCompare compareOp;
        public float compareRef;
    }

    struct GenPhaseParams
    {
        public float screenHeight;
        public float frame;
        public float pps;
        public float ppf;
    }

    struct GenSignalParams
    {
        public bool svideo;
        public float noiseAmount;
        public float time;
        public float c;
        public Vector2 outputResolution;
    }

    struct DecSignal1Params
    {
        public Vector2 outputResolution;
        public float c;
    }

    struct DecSignal2Params
    {
        public float c;
        public bool temporalBlend;
        public Vector2 outputResolution;
    }

    struct SharpenParams
    {
        public float sharpen_amount;
        public float sharpen_resolution;
    }

    struct BlitTVParams
    {
        public Vector2 curvature;
        public Vector2 scale;
        public Vector2 maskScale;
    }

    struct BlitInterlaceParams
    {
        public int frame;
    }

    struct VUDrawCmd
    {
        public int bufferOffset;
        public int bufferLength;
        public VDPTopology topology;
        public bool needsNewRenderPass;
        public Color32? clearColor;
        public float? clearDepth;
        public bool needsNewPso;
        public PipelineSettings psoSettings;
        public bool needsNewTexture;
        public SamplerSettings sampleTU0;
        public SamplerSettings sampleTU1;
        public Texture? tu0;
        public Texture? tu1;
        public FFParams ffParams;
        public VUCData cdata;
        public VUProgram? program;
        public int vuCost;
    }

    public int TextureMemoryUsage => _totalTexMem;

    private readonly GraphicsDevice _graphicsDevice;
    private readonly Texture _screenTarget0;
    private readonly Texture _screenTarget1;
    private readonly Texture _screenTargetCombine;
    private readonly Texture _depthTarget;
    private readonly Texture _blankTexture;

    private readonly Texture _shadowmask;

    private readonly Texture _cb_lut;         // NTSC colorburst phase lookup
    private readonly Texture _signal_tex_1;   // used to generate/decode NTSC signal
    private readonly Texture _signal_tex_2;

    private DreamboxConfig _config;

    private List<byte> _frameVUData;

    private nint _activeCmdBuf = 0;
    private nint _activeRenderPass = 0;
    private nint _activeCopyPass = 0;
    private SDL.SDL_GPUViewport _activeViewport;
    private SDL.SDL_Rect _activeScissor;

    private bool _screenFlip = false;

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

    private Queue<VUDrawCmd> _drawQueue = new Queue<VUDrawCmd>();

    private Queue<IDisposable> _disposeQueue = new Queue<IDisposable>();

    private int _curFrame = 0;

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

    private SamplerSettings _sampleTU0 = new SamplerSettings() {
        filter = VDPFilter.LINEAR,
        wrapU = VDPWrap.REPEAT,
        wrapV = VDPWrap.REPEAT,
    };

    private SamplerSettings _sampleTU1 = new SamplerSettings() {
        filter = VDPFilter.LINEAR,
        wrapU = VDPWrap.REPEAT,
        wrapV = VDPWrap.REPEAT,
    };

    private Texture? _activeTU0 = null;
    private Texture? _activeTU1 = null;

    private VDPTexCombine _texCombine = VDPTexCombine.Mul;
    private VDPTexCombine _vtxCombine = VDPTexCombine.Mul;

    private Shader _blit_vertex;
    private Shader _blit_fragment;
    private Shader _blit_tv;
    private Shader _blit_interlace_fragment;
    private Shader _vu_vertex;
    private Shader _ff_fragment;
    private Shader _gen_phase;
    private Shader _gen_signal;
    private Shader _dec_signal_1;
    private Shader _dec_signal_2;
    private Shader _sharpen;

    private GraphicsPipeline _pso_blit;
    private GraphicsPipeline _pso_blit_tv;
    private GraphicsPipeline _pso_blit_interlace;
    private GraphicsPipeline _pso_gen_phase;
    private GraphicsPipeline _pso_gen_signal;
    private GraphicsPipeline _pso_dec_signal_1;
    private GraphicsPipeline _pso_dec_signal_2;
    private GraphicsPipeline _pso_sharpen;

    private VertexBuffer<BlitVertex> _screenBlitQuad;
    private VertexBuffer<BlitVertex> _internalBlitQuad;

    private nint _linearSampler;
    private nint _shadowmaskSampler;

    private GraphicsBuffer _vu_program;
    private GraphicsBuffer _vu_data;

    private int _totalVuCostThisFrame = 0;

    private VUCData _vu_cdata;
    private VUProgram? _vu_programData;
    private int _vu_programCost;

    private ComputePipeline _convertYuvPipeline;
    private GraphicsBuffer _yuvBuffer;

    private ComputePipeline _depthQueryPipeline;
    private GraphicsBuffer _depthQueryResults;
    private nint _depthQueryFence;

    private uint _frameCount;

    private bool _wireframeMode = false;

    public VDP(DreamboxConfig config, GraphicsDevice graphicsDevice)
    {
        _config = config;

        _graphicsDevice = graphicsDevice;
        
        _screenTarget0 = new Texture(graphicsDevice, SDL.SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_R8G8B8A8_UNORM, SCREEN_WIDTH, SCREEN_HEIGHT, 1,
            SDL.SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_COLOR_TARGET | SDL.SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER);
        _screenTarget1 = new Texture(graphicsDevice, SDL.SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_R8G8B8A8_UNORM, SCREEN_WIDTH, SCREEN_HEIGHT, 1,
            SDL.SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_COLOR_TARGET | SDL.SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER);
        _screenTargetCombine = new Texture(graphicsDevice, SDL.SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_R8G8B8A8_UNORM, SCREEN_WIDTH, SCREEN_HEIGHT, 1,
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

        _cb_lut = new Texture(graphicsDevice, SDL.SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_R16G16_FLOAT, 1, SCREEN_HEIGHT, 1,
            SDL.SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_COLOR_TARGET | SDL.SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER);

        _signal_tex_1 = new Texture(graphicsDevice, SDL.SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_R16G16B16A16_FLOAT, SCREEN_WIDTH * COLORBURST_LENGTH, SCREEN_HEIGHT, 1,
            SDL.SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_COLOR_TARGET | SDL.SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER);

        _signal_tex_2 = new Texture(graphicsDevice, SDL.SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_R16G16B16A16_FLOAT, SCREEN_WIDTH * COLORBURST_LENGTH, SCREEN_HEIGHT, 1,
            SDL.SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_COLOR_TARGET | SDL.SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER);

        _frameVUData = new List<byte>(1024 * 3 * Unsafe.SizeOf<PackedVertex>());

        // load shaders
        _blit_vertex = LoadShader(_graphicsDevice, "content/shaders/blit.vert.spv", SDL.SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_VERTEX, 0, 0, 0);
        _blit_fragment = LoadShader(_graphicsDevice, "content/shaders/blit.frag.spv", SDL.SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_FRAGMENT, 1, 0, 0);
        _blit_tv = LoadShader(_graphicsDevice, "content/shaders/blit_tv.frag.spv", SDL.SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_FRAGMENT, 2, 0, 1);
        _blit_interlace_fragment = LoadShader(_graphicsDevice, "content/shaders/blit_interlace.frag.spv", SDL.SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_FRAGMENT, 2, 0, 1);
        _vu_vertex = LoadShader(_graphicsDevice, "content/shaders/vu.vert.spv", SDL.SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_VERTEX, 0, 1, 1);
        _ff_fragment = LoadShader(_graphicsDevice, "content/shaders/fixedfunction.frag.spv", SDL.SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_FRAGMENT, 2, 0, 1);

        _gen_phase = LoadShader(_graphicsDevice, "content/shaders/gen_phase.frag.spv", SDL.SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_FRAGMENT, 0, 0, 1);
        _gen_signal = LoadShader(_graphicsDevice, "content/shaders/gen_signal.frag.spv", SDL.SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_FRAGMENT, 2, 0, 1);
        _dec_signal_1 = LoadShader(_graphicsDevice, "content/shaders/dec_signal_1.frag.spv", SDL.SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_FRAGMENT, 1, 0, 1);
        _dec_signal_2 = LoadShader(_graphicsDevice, "content/shaders/dec_signal_2.frag.spv", SDL.SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_FRAGMENT, 2, 0, 1);
        _sharpen = LoadShader(_graphicsDevice, "content/shaders/sharpen.frag.spv", SDL.SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_FRAGMENT, 1, 0, 1);

        // load shadowmask texture
        var cmdBuf = SDL.SDL_AcquireGPUCommandBuffer(_graphicsDevice.handle);
        var copyPass = SDL.SDL_BeginGPUCopyPass(cmdBuf);
        {
            using (var texFile = File.OpenRead("content/shadowmask.dds"))
            using (var texFileReader = new BinaryReader(texFile))
            {
                _shadowmask = Texture.ParseDDS(graphicsDevice, texFileReader, copyPass, cmdBuf);
            }
        }
        SDL.SDL_EndGPUCopyPass(copyPass);
        SDL.SDL_SubmitGPUCommandBuffer(cmdBuf);

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

        _pso_blit_tv = GraphicsPipeline.Create<BlitVertex>(graphicsDevice,
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
            }], SDL.SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_INVALID, false, _blit_vertex, _blit_tv,
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

        _pso_blit_interlace = GraphicsPipeline.Create<BlitVertex>(graphicsDevice,
            [new () {
                format = _screenTargetCombine.format,
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
            }], SDL.SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_INVALID, false, _blit_vertex, _blit_interlace_fragment,
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

        _pso_gen_phase = GraphicsPipeline.Create<BlitVertex>(graphicsDevice,
            [new () {
                format = _cb_lut.format,
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
            }], SDL.SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_INVALID, false, _blit_vertex, _gen_phase,
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

        _pso_gen_signal = GraphicsPipeline.Create<BlitVertex>(graphicsDevice,
            [new () {
                format = _signal_tex_1.format,
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
            }], SDL.SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_INVALID, false, _blit_vertex, _gen_signal,
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

        _pso_dec_signal_1 = GraphicsPipeline.Create<BlitVertex>(graphicsDevice,
            [new () {
                format = _signal_tex_1.format,
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
            }], SDL.SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_INVALID, false, _blit_vertex, _dec_signal_1,
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

        _pso_dec_signal_2 = GraphicsPipeline.Create<BlitVertex>(graphicsDevice,
            [new () {
                format = _signal_tex_1.format,
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
            }], SDL.SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_INVALID, false, _blit_vertex, _dec_signal_2,
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

        _pso_sharpen = GraphicsPipeline.Create<BlitVertex>(graphicsDevice,
            [new () {
                format = _signal_tex_1.format,
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
            }], SDL.SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_INVALID, false, _blit_vertex, _sharpen,
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

        _convertYuvPipeline = new ComputePipeline(graphicsDevice, File.ReadAllBytes("content/shaders/convert_yuv.spv"), "main", 0, 0, 1, 1, 0, 1, 1, 1, 1);

        // enough of a buffer to convert a 1024x1024 YUV image
        _yuvBuffer = new GraphicsBuffer(graphicsDevice, (1024 * 1024) + (512 * 512 * 2), SDL.SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_COMPUTE_STORAGE_READ);

        _depthQueryPipeline = new ComputePipeline(graphicsDevice, File.ReadAllBytes("content/shaders/depth_query.spv"), "main", 1, 0, 0, 0, 1, 1, 1, 1, 1);

        // big enough buffer to store output results of depth query (uint)
        _depthQueryResults = new GraphicsBuffer(graphicsDevice, 4, SDL.SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_COMPUTE_STORAGE_WRITE);

        // enough of a buffer to convert a 1024x1024 YUV image
        _yuvBuffer = new GraphicsBuffer(graphicsDevice, (1024 * 1024) + (512 * 512 * 2), SDL.SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_COMPUTE_STORAGE_READ);

        // vertex buffers
        _screenBlitQuad = new VertexBuffer<BlitVertex>(graphicsDevice, 6);
        _internalBlitQuad = new VertexBuffer<BlitVertex>(graphicsDevice, 6);

        SetBlitQuad(_internalBlitQuad, 1.0f, 1.0f);

        SDL.SDL_SetGPUBufferName(_graphicsDevice.handle, _screenBlitQuad.handle, nameof(_screenBlitQuad));

        // enough space to fit 64 VU instructions (32 bits per instruction)
        _vu_program = new GraphicsBuffer(graphicsDevice, 4 * MAX_VU_PROGRAM_SIZE, SDL.SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_GRAPHICS_STORAGE_READ);

        // note: will be resized as needed
        _vu_data = new GraphicsBuffer(graphicsDevice, 1024 * 3 * Unsafe.SizeOf<PackedVertex>(), SDL.SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_VERTEX);
        
        // samplers
        _linearSampler = SDL.SDL_CreateGPUSampler(graphicsDevice.handle, new SDL.SDL_GPUSamplerCreateInfo() {
            min_filter = SDL.SDL_GPUFilter.SDL_GPU_FILTER_LINEAR,
            mag_filter = SDL.SDL_GPUFilter.SDL_GPU_FILTER_LINEAR,
            address_mode_u = SDL.SDL_GPUSamplerAddressMode.SDL_GPU_SAMPLERADDRESSMODE_CLAMP_TO_EDGE,
            address_mode_v = SDL.SDL_GPUSamplerAddressMode.SDL_GPU_SAMPLERADDRESSMODE_CLAMP_TO_EDGE,
        });

        _shadowmaskSampler = SDL.SDL_CreateGPUSampler(graphicsDevice.handle, new SDL.SDL_GPUSamplerCreateInfo() {
            min_filter = SDL.SDL_GPUFilter.SDL_GPU_FILTER_LINEAR,
            mag_filter = SDL.SDL_GPUFilter.SDL_GPU_FILTER_LINEAR,
            mipmap_mode = SDL.SDL_GPUSamplerMipmapMode.SDL_GPU_SAMPLERMIPMAPMODE_LINEAR,
            address_mode_u = SDL.SDL_GPUSamplerAddressMode.SDL_GPU_SAMPLERADDRESSMODE_REPEAT,
            address_mode_v = SDL.SDL_GPUSamplerAddressMode.SDL_GPU_SAMPLERADDRESSMODE_REPEAT,
        });

        ResetCompat();
    }

    public void ResetCompat()
    {
        _activeTU0 = null;
        _activeTU1 = null;

        // default VU settings for backwards compat
        _drawSettings.vuStride = 32;
        _drawSettings.vuLayout[0] = new VDPVertexSlot() {
            offset = 0,
            format = VDPVertexSlotFormat.Float4
        };
        _drawSettings.vuLayout[1] = new VDPVertexSlot() {
            offset = 16,
            format = VDPVertexSlotFormat.Float2
        };
        _drawSettings.vuLayout[2] = new VDPVertexSlot() {
            offset = 24,
            format = VDPVertexSlotFormat.UNorm4
        };
        _drawSettings.vuLayout[3] = new VDPVertexSlot() {
            offset = 28,
            format = VDPVertexSlotFormat.UNorm4
        };

        /* default VU program: simply copies input vertices to output
            ld r0 0                     # load input slot 0 into r0
            ld r1 1                     # load input slot 1 into r1
            ld r2 2                     # load input slot 2 into r2
            ld r3 3                     # load input slot 3 into r3
            shf r1 r1 [0 1 0 1] 0b1111  # r1 = r1.xyxy (note: xy is used for tu0, zw is used for tu1)
            st pos r0                   # store r0 to output position slot
            st tex r1                   # store r1 to output texcoord slot
            st col r2                   # store r2 to output color slot
            st ocol r3                  # store r3 to output ocolor slot
            end
        */
        Span<uint> defaultVuProg = [
            EncodeVUInstr(0, 0, 0),
            EncodeVUInstr(0, 1, 1),
            EncodeVUInstr(0, 2, 2),
            EncodeVUInstr(0, 3, 3),
            EncodeVUInstr(14, 1, 1, 0, 1, 0, 1, 0b1111),
            EncodeVUInstr(1, 0, 0),
            EncodeVUInstr(1, 1, 1),
            EncodeVUInstr(1, 2, 2),
            EncodeVUInstr(1, 3, 3),
            EncodeVUInstr(15, 0, 0),
        ];

        var cmdBuf = SDL.SDL_AcquireGPUCommandBuffer(_graphicsDevice.handle);
        var copyPass = SDL.SDL_BeginGPUCopyPass(cmdBuf);
        {
            _blankTexture.SetData(copyPass, cmdBuf, [ new Color32(255, 255, 255) ], 0, 0, 0, 0, 0, 1, 1, 1, false);
            _vu_program.SetData(copyPass, defaultVuProg, 0, false);
        }
        SDL.SDL_EndGPUCopyPass(copyPass);
        SDL.SDL_SubmitGPUCommandBuffer(cmdBuf);
    }

    public void SetWireframe(bool enabled)
    {
        _wireframeMode = enabled;

        // flush the pipeline cache to force pipelines to be rebuilt with new fill mode
        foreach (var kvp in _pipelineCache)
        {
            kvp.Value.Dispose();
        }
        _pipelineCache.Clear();
    }

    private Shader LoadShader(GraphicsDevice device, string path, SDL.SDL_GPUShaderStage stage,
        int numSamplers, int numStorageBuffers, int numUniformBuffers)
    {
        Console.WriteLine("Loading shader: " + path);
        byte[] data = File.ReadAllBytes(path);
        return new Shader(device, data, "main", stage, numSamplers, numStorageBuffers, numUniformBuffers);
    }

    private Texture CurrentScreenTarget()
    {
        return _screenFlip ? _screenTarget1 : _screenTarget0;
    }

    private Texture PreviousScreenTarget()
    {
        return _screenFlip ? _screenTarget0 : _screenTarget1;
    }

    private void FlushDrawQueue()
    {
        if (_vu_data.byteLength < _frameVUData.Count)
        {
            _vu_data.Dispose();
            _vu_data = new GraphicsBuffer(_graphicsDevice, _frameVUData.Capacity, SDL.SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_VERTEX);
        }

        if (_activeCopyPass != 0)
        {
            throw new Exception("oops");
        }

        var copyPass = SDL.SDL_BeginGPUCopyPass(_activeCmdBuf);
        _vu_data.SetData(copyPass, CollectionsMarshal.AsSpan(_frameVUData), 0, true);
        SDL.SDL_EndGPUCopyPass(copyPass);

        while (_drawQueue.TryDequeue(out var cmd))
        {
            _totalVuCostThisFrame += cmd.vuCost;

            // this draw requires a new vertex program to be copied into the buffer
            if (cmd.program != null)
            {
                FlushRenderPass();
                copyPass = SDL.SDL_BeginGPUCopyPass(_activeCmdBuf);

                var prg = cmd.program.Value;
                _vu_program.SetData(copyPass, prg[0..MAX_VU_PROGRAM_SIZE], 0, true);

                SDL.SDL_EndGPUCopyPass(copyPass);
            }

            if (cmd.needsNewRenderPass)
            {
                FlushRenderPass();
                _clearColor = cmd.clearColor;
                _clearDepth = cmd.clearDepth;
            }
            CheckRenderPass();

            if (cmd.needsNewPso)
            {
                var pipelineSettings = cmd.psoSettings;
                pipelineSettings.topology = cmd.topology;

                var pso = GetOrCreatePipeline(pipelineSettings);
                SDL.SDL_BindGPUGraphicsPipeline(_activeRenderPass, pso.handle);
            }

            SDL.SDL_BindGPUVertexBuffers(_activeRenderPass, 0, [
                new SDL.SDL_GPUBufferBinding() {
                    buffer = _vu_data.handle,
                    offset = (uint)cmd.bufferOffset
                }
            ], 1);

            SDL.SDL_BindGPUVertexStorageBuffers(_activeRenderPass, 0, [
                _vu_program.handle
            ], 1);

            if (cmd.needsNewTexture)
            {
                SDL.SDL_BindGPUFragmentSamplers(_activeRenderPass, 0, [
                    new SDL.SDL_GPUTextureSamplerBinding() {
                        texture = cmd.tu0?.handle ?? _blankTexture.handle,
                        sampler = GetOrCreateSampler(cmd.sampleTU0)
                    },
                    new SDL.SDL_GPUTextureSamplerBinding() {
                        texture = cmd.tu1?.handle ?? _blankTexture.handle,
                        sampler = GetOrCreateSampler(cmd.sampleTU1)
                    }
                ], 2);
            }

            unsafe
            {
                VUCData* cdata_ptr = &cmd.cdata;
                SDL.SDL_PushGPUVertexUniformData(_activeCmdBuf, 0, (nint)cdata_ptr, (uint)Unsafe.SizeOf<VUCData>());

                FFParams* ffParams_ptr = &cmd.ffParams;
                SDL.SDL_PushGPUFragmentUniformData(_activeCmdBuf, 0, (nint)ffParams_ptr, (uint)Unsafe.SizeOf<FFParams>());
            }

            int vtxCount = cmd.bufferLength / cmd.psoSettings.vuStride;
            SDL.SDL_DrawGPUPrimitives(_activeRenderPass, (uint)vtxCount, 1, 0, 0);
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

            var screenTarget = CurrentScreenTarget();

            // start new render pass
            _activeRenderPass = SDL.SDL_BeginGPURenderPass(_activeCmdBuf, [
                new SDL.SDL_GPUColorTargetInfo() {
                    texture = _drawSettings.target?.texture.handle ?? screenTarget.handle,
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

    private SDL.SDL_GPUVertexAttribute ConvertLayoutSlot(uint loc, in VDPVertexSlot slot)
    {
        var fmt = slot.format switch
        {
            VDPVertexSlotFormat.Float1 => SDL.SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT,
            VDPVertexSlotFormat.Float2 => SDL.SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT2,
            VDPVertexSlotFormat.Float3 => SDL.SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT3,
            VDPVertexSlotFormat.Float4 => SDL.SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT4,
            VDPVertexSlotFormat.UNorm4 => SDL.SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_UBYTE4_NORM,
            VDPVertexSlotFormat.SNorm4 => SDL.SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_BYTE4_NORM,
            _ => SDL.SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_INVALID,// invalid format
        };

        return new SDL.SDL_GPUVertexAttribute() {
            location = loc,
            buffer_slot = 0,
            offset = (uint)slot.offset,
            format = fmt,
        };
    }

    private GraphicsPipeline GetOrCreatePipeline(PipelineSettings settings)
    {
        if (_pipelineCache.TryGetValue(settings, out var pso))
        {
            return pso;
        }

        // create layout from vu settings
        var vtxDesc = new SDL.SDL_GPUVertexBufferDescription() {
            slot = 0,
            pitch = (uint)settings.vuStride,
            input_rate = SDL.SDL_GPUVertexInputRate.SDL_GPU_VERTEXINPUTRATE_VERTEX,
            instance_step_rate = 0
        };

        Span<SDL.SDL_GPUVertexAttribute> vtxAttr = [
            ConvertLayoutSlot(0, settings.vuLayout[0]),
            ConvertLayoutSlot(1, settings.vuLayout[1]),
            ConvertLayoutSlot(2, settings.vuLayout[2]),
            ConvertLayoutSlot(3, settings.vuLayout[3]),
            ConvertLayoutSlot(4, settings.vuLayout[4]),
            ConvertLayoutSlot(5, settings.vuLayout[5]),
            ConvertLayoutSlot(6, settings.vuLayout[6]),
            ConvertLayoutSlot(7, settings.vuLayout[7]),
        ];

        // create new pipeline
        pso = new GraphicsPipeline(_graphicsDevice,
            [new () {
                format = settings.target?.texture.format ?? _screenTarget0.format,
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
            }], settings.target?.depthTexture.format ?? _depthTarget.format, true, _vu_vertex, _ff_fragment,
            vtxDesc, vtxAttr,
            TOPOLOGY_TO_SDL[settings.topology],
            new () {
                fill_mode = _wireframeMode ? SDL.SDL_GPUFillMode.SDL_GPU_FILLMODE_LINE : SDL.SDL_GPUFillMode.SDL_GPU_FILLMODE_FILL,
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
        _frameVUData.Clear();
        _totalVuCostThisFrame = 0;

        SDL.SDL_PushGPUDebugGroup(cmdBuf, $"VDP FRAME {_frameCount++}");
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

    public void SetSampleParams(int slot, VDPFilter filter, VDPWrap wrapU, VDPWrap wrapV)
    {
        _needsNewTexture = true;

        switch (slot)
        {
            case 0: {
                _sampleTU0.filter = filter;
                _sampleTU0.wrapU = wrapU;
                _sampleTU0.wrapV = wrapV;
                break;
            }
            case 1: {
                _sampleTU1.filter = filter;
                _sampleTU1.wrapU = wrapU;
                _sampleTU1.wrapV = wrapV;
                break;
            }
        }
    }

    public int AllocTexture(bool mipmap, VDPTextureFormat format, int width, int height)
    {
        VdpTexture vdpTex;

        if (format == VDPTextureFormat.YUV420)
        {
            // width and height must be divisible by 2
            if ((width % 2) != 0 || (height % 2) != 0)
            {
                Console.WriteLine("YUV texture dimensions must be divisible by 2");
                return -1;
            }

            // mipmapping not allowed
            if (mipmap)
            {
                Console.WriteLine("Mipmapping on YUV textures unsupported");
                return -1;
            }

            int totalSize = VdpTexture.CalcTextureTotalSize(format, width, height, 1);

            if (_totalTexMem + totalSize >= MAX_TEXTURE_MEM)
            {
                Console.WriteLine("Texture memory exhausted");
                return -1;
            }

            SDL.SDL_GPUTextureUsageFlags flags = SDL.SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER |
                SDL.SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_COMPUTE_STORAGE_WRITE;

            Texture texture = new Texture(_graphicsDevice, FORMAT_TO_SDL[format], width, height, 1, flags);
            vdpTex = new VdpTexture(format, texture);

            _totalTexMem += totalSize;
        }
        else
        {
            // width and height must be power of two
            if ((width & (width - 1)) != 0 || (height & (height - 1)) != 0)
            {
                Console.WriteLine("Non-power-of-two texture unsupported");
                return -1;
            }

            int levelcount = mipmap ? VdpTexture.GetTotalLevelCount(width, height) : 1;
            int totalSize = VdpTexture.CalcTextureTotalSize(format, width, height, levelcount);

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

        int totalSize = VdpTexture.CalcTextureTotalSize(VDPTextureFormat.RGBA8888, width, height, 1) + VdpRenderTexture.CalcDepthTextureTotalSize(width, height, 1);

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

        if (_texCache[handle] is VdpTexture tex)
        {
            _disposeQueue.Enqueue(tex);
        }

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

        if (target.format != VDPTextureFormat.RGBA8888)
        {
            Console.WriteLine("Attempted to copy framebuffer to texture with invalid format: " + handle);
            return;
        }

        var screenTarget = _drawSettings.target?.texture ?? CurrentScreenTarget();

        FlushRenderPass();
        SDL.SDL_BlitGPUTexture(_activeCmdBuf, new SDL.SDL_GPUBlitInfo() {
            source = new SDL.SDL_GPUBlitRegion() {
                texture = screenTarget.handle,
                x = (uint)srcRect.x,
                y = (uint)srcRect.y,
                w = (uint)srcRect.w,
                h = (uint)srcRect.h
            },
            destination = new SDL.SDL_GPUBlitRegion() {
                texture = target.texture.handle,
                x = (uint)dstRect.x,
                y = (uint)dstRect.y,
                w = (uint)dstRect.w,
                h = (uint)dstRect.h
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
        if (handle < 0 || handle >= _texCache.Count)
        {
            Console.WriteLine("Attempted to upload data to invalid texture handle: " + handle);
            return;
        }

        if (_texCache[handle] is VdpTexture texture && texture.format == VDPTextureFormat.YUV420)
        {
            // ensure there's enough space in the buffer
            if (_yuvBuffer.byteLength < yData.Length + uData.Length + vData.Length)
            {
                _yuvBuffer.Dispose();
                _yuvBuffer = new GraphicsBuffer(_graphicsDevice, yData.Length + uData.Length + vData.Length, SDL.SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_COMPUTE_STORAGE_READ);
            }

            // check if we need to end current render pass & start new copy pass
            CheckCopyPass();

            // copy data into buffer
            _yuvBuffer.SetData(_activeCopyPass, yData, 0, true);
            _yuvBuffer.SetData(_activeCopyPass, uData, yData.Length, false);
            _yuvBuffer.SetData(_activeCopyPass, vData, yData.Length + uData.Length, false);

            FlushCopyPass();

            nint computePass = SDL.SDL_BeginGPUComputePass(_activeCmdBuf, [
                new SDL.SDL_GPUStorageTextureReadWriteBinding() {
                    texture = texture.texture.handle,
                    mip_level = 0,
                    layer = 0,
                    cycle = true
                }
            ], 1, [], 0);

            SDL.SDL_BindGPUComputePipeline(computePass, _convertYuvPipeline.handle);

            SDL.SDL_BindGPUComputeStorageBuffers(computePass, 0, [_yuvBuffer.handle], 1);

            ConvertYUVParams p = new ConvertYUVParams() {
                imgWidth = (uint)texture.texture.width,
                imgHeight = (uint)texture.texture.height
            };

            unsafe
            {
                ConvertYUVParams* pPtr = &p;
                SDL.SDL_PushGPUComputeUniformData(_activeCmdBuf, 0, (nint)pPtr, (uint)Unsafe.SizeOf<ConvertYUVParams>());
            }

            // invoke compute shader
            SDL.SDL_DispatchGPUCompute(computePass, 1, 1, 1);

            // done
            SDL.SDL_EndGPUComputePass(computePass);
        }
        else
        {
            Console.WriteLine("Attempted to upload data to invalid texture handle: " + handle);
        }
    }

    // TODO:
    // I think this should work, but it is COMPLETELY untested

    public void SubmitDepthQuery(float refVal, VDPCompare compare, SDL.SDL_Rect rect)
    {
        // oops, there was an old depth query in flight already. oh well.
        if (_depthQueryFence != 0)
        {
            SDL.SDL_ReleaseGPUFence(_graphicsDevice.handle, _depthQueryFence);
        }

        // we record a brand new command buffer for this query instead of using the main one
        // that way, we can immediately submit the command buffer and grab a fence for the depth query
        // this does mean depth results will be a frame behind (as this command buffer will be executed before the main command buffer)

        nint cmdBuf = SDL.SDL_AcquireGPUCommandBuffer(_graphicsDevice.handle);

        nint computePass = SDL.SDL_BeginGPUComputePass(cmdBuf, [], 0, [
            new SDL.SDL_GPUStorageBufferReadWriteBinding() {
                buffer = _depthQueryResults.handle,
                cycle = true
            }
        ], 1);

        SDL.SDL_BindGPUComputePipeline(computePass, _depthQueryPipeline.handle);

        SDL.SDL_BindGPUComputeSamplers(computePass, 0, [
            new SDL.SDL_GPUTextureSamplerBinding() {
                texture = _depthTarget.handle,
                sampler = _linearSampler,
            }
        ], 1);

        DepthQueryParams p = new DepthQueryParams() {
            rect_x = rect.x,
            rect_y = rect.y,
            rect_w = rect.w,
            rect_h = rect.h,
            compareOp = compare,
            compareRef = refVal,
        };

        unsafe
        {
            DepthQueryParams* pPtr = &p;
            SDL.SDL_PushGPUComputeUniformData(cmdBuf, 0, (nint)pPtr, (uint)Unsafe.SizeOf<DepthQueryParams>());
        }

        // invoke compute shader
        SDL.SDL_DispatchGPUCompute(computePass, 1, 1, 1);
        SDL.SDL_EndGPUComputePass(computePass);

        // download results back into transfer buffer
        nint copyPass = SDL.SDL_BeginGPUCopyPass(cmdBuf);
        _depthQueryResults.GetData<uint>(copyPass, 0, 1);
        SDL.SDL_EndGPUCopyPass(copyPass);

        // submit & grab a fence!
        _depthQueryFence = SDL.SDL_SubmitGPUCommandBufferAndAcquireFence(cmdBuf);
    }

    public int GetDepthQueryResult()
    {
        if (_depthQueryFence == 0)
        {
            Console.WriteLine("Attempted to get the results of a depth query, but no query has been submitted");
            return 0;
        }

        // wait until the query has finished
        SDL.SDL_WaitForGPUFences(_graphicsDevice.handle, true, [_depthQueryFence], 1);
        SDL.SDL_ReleaseGPUFence(_graphicsDevice.handle, _depthQueryFence);
        _depthQueryFence = 0;

        Span<int> result = [0];
        _depthQueryResults.ReadData(result);
        return result[0];
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

    public void BindTexture(int slot, int handle)
    {
        Texture? value;

        if (handle == -1)
        {
            value = null;
        }
        else if (handle < 0 || handle >= _texCache.Count)
        {
            Console.WriteLine("Attempted to bind invalid texture handle: " + handle);
            return;
        }
        else
        {
            value = _texCache[handle]?.texture;
        }

        switch (slot) {
            case 0: {
                _activeTU0 = value;
                break;
            }
            case 1: {
                _activeTU1 = value;
                break;
            }
        }

        _needsNewTexture = true;
    }

    public void SetTexCombine(VDPTexCombine texCombine, VDPTexCombine vtxCombine)
    {
        _texCombine = texCombine;
        _vtxCombine = vtxCombine;
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

    public void SetVUCData(int offset, Vector4 data)
    {
        _vu_cdata[offset] = data;
    }

    public void SetVULayout(int slot, int offset, VDPVertexSlotFormat format)
    {
        _needsNewPipeline = true;
        _drawSettings.vuLayout[slot].offset = offset;
        _drawSettings.vuLayout[slot].format = format;
    }

    public void SetVUStride(int stride)
    {
        _needsNewPipeline = true;
        _drawSettings.vuStride = stride;
    }

    public void UploadVUProgram(Span<uint> program)
    {
        var prg = new VUProgram();
        program.CopyTo(prg[0..MAX_VU_PROGRAM_SIZE]);

        _vu_programData = prg;

        // measure the cost of the VU program
        _vu_programCost = MAX_VU_PROGRAM_SIZE;
        for (int i = 0; i < program.Length; i++)
        {
            uint op = program[i] & 0xF;
            if (op == 15)
            {
                _vu_programCost = i + 1;
                break;
            }
        }
    }

    public void SubmitVU(VDPTopology topology, Span<byte> data)
    {
        FlushCopyPass();

        int bufferOffset = _frameVUData.Count;
        int bufferLen = data.Length;

        _frameVUData.AddRange(data);

        int numVertices = bufferLen / _drawSettings.vuStride;

        // queue draw command
        _drawQueue.Enqueue(new VUDrawCmd() {
            bufferOffset = bufferOffset,
            bufferLength = bufferLen,
            topology = topology,
            needsNewRenderPass = _needsNewRenderPass,
            clearColor = _clearColor,
            clearDepth = _clearDepth,
            needsNewPso = _needsNewPipeline,
            psoSettings = _drawSettings,
            needsNewTexture = _needsNewTexture,
            sampleTU0 = _sampleTU0,
            sampleTU1 = _sampleTU1,
            tu0 = _activeTU0,
            tu1 = _activeTU1,
            ffParams = new FFParams() {
                texCombine = (uint)_texCombine,
                vtxCombine = (uint)_vtxCombine
            },
            cdata = _vu_cdata,
            program = _vu_programData,
            vuCost = _vu_programCost * numVertices,
        });

        _needsNewPipeline = false;
        _needsNewTexture = false;
        _needsNewRenderPass = false;
        _clearColor = null;
        _clearDepth = null;
        _vu_programData = null;
    }

    public void EndFrame(out int numSkipFrames)
    {
        FlushCopyPass();
        FlushDrawQueue();
        FlushRenderPass();

        while (_disposeQueue.TryDequeue(out var res))
        {
            res.Dispose();
        }

        numSkipFrames = _totalVuCostThisFrame / VU_CYCLES_PER_FRAME;

        SDL.SDL_PopGPUDebugGroup(_activeCmdBuf);

        nint renderPass;

        var srcTex = CurrentScreenTarget();
        if (_config.InterlacedVideo)
        {
            // interlace cur+prev frames into single image
            renderPass = SDL.SDL_BeginGPURenderPass(_activeCmdBuf, [
                new SDL.SDL_GPUColorTargetInfo() {
                    texture = _screenTargetCombine.handle,
                    load_op = SDL.SDL_GPULoadOp.SDL_GPU_LOADOP_DONT_CARE,
                    store_op = SDL.SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
                    cycle = true
                }
            ], 1, Unsafe.NullRef<SDL.SDL_GPUDepthStencilTargetInfo>());
            {
                // set up render state
                SDL.SDL_BindGPUGraphicsPipeline(renderPass, _pso_blit_interlace.handle);
                SDL.SDL_BindGPUVertexBuffers(renderPass, 0, [new () {
                    buffer = _internalBlitQuad.handle,
                    offset = 0
                }], 1);
                SDL.SDL_BindGPUFragmentSamplers(renderPass, 0, [
                    new () {
                        texture = _screenTarget0.handle,
                        sampler = _linearSampler
                    },
                    new () {
                        texture = _screenTarget1.handle,
                        sampler = _linearSampler
                    }
                ], 2);

                BlitInterlaceParams ubo;
                ubo.frame = _curFrame;

                unsafe
                {
                    SDL.SDL_PushGPUFragmentUniformData(_activeCmdBuf, 0, (nint)(&ubo), (uint)Unsafe.SizeOf<BlitInterlaceParams>());
                }

                // draw quad
                SDL.SDL_DrawGPUPrimitives(renderPass, 6, 1, 0, 0);
            }
            SDL.SDL_EndGPURenderPass(renderPass);

            srcTex = _screenTargetCombine;
        }

        if (_config.VideoMode == DreamboxVideoMode.Composite || _config.VideoMode == DreamboxVideoMode.SVideo)
        {
            // generate colorburst phase LUT
            renderPass = SDL.SDL_BeginGPURenderPass(_activeCmdBuf, [
                new SDL.SDL_GPUColorTargetInfo() {
                    texture = _cb_lut.handle,
                    load_op = SDL.SDL_GPULoadOp.SDL_GPU_LOADOP_DONT_CARE,
                    store_op = SDL.SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
                    cycle = true
                }
            ], 1, Unsafe.NullRef<SDL.SDL_GPUDepthStencilTargetInfo>());
            {
                // set up render state
                SDL.SDL_BindGPUGraphicsPipeline(renderPass, _pso_gen_phase.handle);
                SDL.SDL_BindGPUVertexBuffers(renderPass, 0, [new () {
                    buffer = _internalBlitQuad.handle,
                    offset = 0
                }], 1);

                GenPhaseParams ubo;
                ubo.screenHeight = SCREEN_HEIGHT;
                ubo.ppf = COLORBURST_PPF;
                ubo.pps = COLORBURST_PPS;
                ubo.frame = _curFrame + 1;

                unsafe
                {
                    SDL.SDL_PushGPUFragmentUniformData(_activeCmdBuf, 0, (nint)(&ubo), (uint)Unsafe.SizeOf<GenPhaseParams>());
                }

                // draw quad
                SDL.SDL_DrawGPUPrimitives(renderPass, 6, 1, 0, 0);
            }
            SDL.SDL_EndGPURenderPass(renderPass);

            // generate NTSC signal
            renderPass = SDL.SDL_BeginGPURenderPass(_activeCmdBuf, [
                new SDL.SDL_GPUColorTargetInfo() {
                    texture = _signal_tex_1.handle,
                    load_op = SDL.SDL_GPULoadOp.SDL_GPU_LOADOP_DONT_CARE,
                    store_op = SDL.SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
                    cycle = true
                }
            ], 1, Unsafe.NullRef<SDL.SDL_GPUDepthStencilTargetInfo>());
            {
                // set up render state
                SDL.SDL_BindGPUGraphicsPipeline(renderPass, _pso_gen_signal.handle);
                SDL.SDL_BindGPUVertexBuffers(renderPass, 0, [new () {
                    buffer = _internalBlitQuad.handle,
                    offset = 0
                }], 1);

                SDL.SDL_BindGPUFragmentSamplers(renderPass, 0, [
                    new () {
                        texture = srcTex.handle,
                        sampler = _linearSampler
                    },
                    new () {
                        texture = _cb_lut.handle,
                        sampler = _linearSampler
                    },
                ], 2);

                GenSignalParams ubo;
                ubo.svideo = _config.VideoMode == DreamboxVideoMode.SVideo;
                ubo.noiseAmount = SIGNAL_NOISE;
                ubo.time = _curFrame / 60.0f;
                ubo.c = COLORBURST_LENGTH;
                ubo.outputResolution = new Vector2(_signal_tex_1.width, _signal_tex_1.height);

                unsafe
                {
                    SDL.SDL_PushGPUFragmentUniformData(_activeCmdBuf, 0, (nint)(&ubo), (uint)Unsafe.SizeOf<GenSignalParams>());
                }

                // draw quad
                SDL.SDL_DrawGPUPrimitives(renderPass, 6, 1, 0, 0);
            }
            SDL.SDL_EndGPURenderPass(renderPass);

            Texture decodeSrc = _signal_tex_1;
            Texture decodeDst = _signal_tex_2;

            if (_config.VideoMode == DreamboxVideoMode.Composite)
            {
                // decode NTSC signal (pass 1)
                renderPass = SDL.SDL_BeginGPURenderPass(_activeCmdBuf, [
                    new SDL.SDL_GPUColorTargetInfo() {
                        texture = _signal_tex_2.handle,
                        load_op = SDL.SDL_GPULoadOp.SDL_GPU_LOADOP_DONT_CARE,
                        store_op = SDL.SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
                        cycle = true
                    }
                ], 1, Unsafe.NullRef<SDL.SDL_GPUDepthStencilTargetInfo>());
                {
                    // set up render state
                    SDL.SDL_BindGPUGraphicsPipeline(renderPass, _pso_dec_signal_1.handle);
                    SDL.SDL_BindGPUVertexBuffers(renderPass, 0, [new () {
                        buffer = _internalBlitQuad.handle,
                        offset = 0
                    }], 1);

                    SDL.SDL_BindGPUFragmentSamplers(renderPass, 0, [
                        new () {
                            texture = _signal_tex_1.handle,
                            sampler = _linearSampler
                        }
                    ], 1);

                    DecSignal1Params ubo;
                    ubo.c = COLORBURST_LENGTH;
                    ubo.outputResolution = new Vector2(_signal_tex_2.width, _signal_tex_2.height);

                    unsafe
                    {
                        SDL.SDL_PushGPUFragmentUniformData(_activeCmdBuf, 0, (nint)(&ubo), (uint)Unsafe.SizeOf<DecSignal1Params>());
                    }

                    // draw quad
                    SDL.SDL_DrawGPUPrimitives(renderPass, 6, 1, 0, 0);
                }
                SDL.SDL_EndGPURenderPass(renderPass);

                decodeSrc = _signal_tex_2;
                decodeDst = _signal_tex_1;
            }

            // decode NTSC signal (pass 2)
            renderPass = SDL.SDL_BeginGPURenderPass(_activeCmdBuf, [
                new SDL.SDL_GPUColorTargetInfo() {
                    texture = decodeDst.handle,
                    load_op = SDL.SDL_GPULoadOp.SDL_GPU_LOADOP_DONT_CARE,
                    store_op = SDL.SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
                    cycle = true
                }
            ], 1, Unsafe.NullRef<SDL.SDL_GPUDepthStencilTargetInfo>());
            {
                // set up render state
                SDL.SDL_BindGPUGraphicsPipeline(renderPass, _pso_dec_signal_2.handle);
                SDL.SDL_BindGPUVertexBuffers(renderPass, 0, [new () {
                    buffer = _internalBlitQuad.handle,
                    offset = 0
                }], 1);

                SDL.SDL_BindGPUFragmentSamplers(renderPass, 0, [
                    new () {
                        texture = decodeSrc.handle,
                        sampler = _linearSampler
                    },
                    new () {
                        texture = _cb_lut.handle,
                        sampler = _linearSampler
                    }
                ], 2);

                DecSignal2Params ubo;
                ubo.c = COLORBURST_LENGTH;
                ubo.temporalBlend = true;
                ubo.outputResolution = new Vector2(_signal_tex_1.width, _signal_tex_1.height);

                unsafe
                {
                    SDL.SDL_PushGPUFragmentUniformData(_activeCmdBuf, 0, (nint)(&ubo), (uint)Unsafe.SizeOf<DecSignal2Params>());
                }

                // draw quad
                SDL.SDL_DrawGPUPrimitives(renderPass, 6, 1, 0, 0);
            }
            SDL.SDL_EndGPURenderPass(renderPass);

            // sharpen
            renderPass = SDL.SDL_BeginGPURenderPass(_activeCmdBuf, [
                new SDL.SDL_GPUColorTargetInfo() {
                    texture = decodeSrc.handle,
                    load_op = SDL.SDL_GPULoadOp.SDL_GPU_LOADOP_DONT_CARE,
                    store_op = SDL.SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
                    cycle = true
                }
            ], 1, Unsafe.NullRef<SDL.SDL_GPUDepthStencilTargetInfo>());
            {
                // set up render state
                SDL.SDL_BindGPUGraphicsPipeline(renderPass, _pso_sharpen.handle);
                SDL.SDL_BindGPUVertexBuffers(renderPass, 0, [new () {
                    buffer = _internalBlitQuad.handle,
                    offset = 0
                }], 1);

                SDL.SDL_BindGPUFragmentSamplers(renderPass, 0, [
                    new () {
                        texture = decodeDst.handle,
                        sampler = _linearSampler
                    },
                    new () {
                        texture = _cb_lut.handle,
                        sampler = _linearSampler
                    }
                ], 2);

                SharpenParams ubo;
                ubo.sharpen_amount = _config.VideoMode == DreamboxVideoMode.Composite ? 0.3f : 0.15f;
                ubo.sharpen_resolution = 640.0f;

                unsafe
                {
                    SDL.SDL_PushGPUFragmentUniformData(_activeCmdBuf, 0, (nint)(&ubo), (uint)Unsafe.SizeOf<SharpenParams>());
                }

                // draw quad
                SDL.SDL_DrawGPUPrimitives(renderPass, 6, 1, 0, 0);
            }
            SDL.SDL_EndGPURenderPass(renderPass);
        }

        _curFrame++;
        _screenFlip = !_screenFlip;
    }

    public void BlitToScreen(nint cmdBuf, nint renderPass, float widthScale, float heightScale)
    {
        // set up blit quad
        SetBlitQuad(_screenBlitQuad, widthScale, heightScale);

        Texture srcTex;
        if (_config.VideoMode == DreamboxVideoMode.Default || _config.VideoMode == DreamboxVideoMode.VGA)
        {
            srcTex = _config.InterlacedVideo ? _screenTargetCombine : PreviousScreenTarget();
        }
        else if (_config.VideoMode == DreamboxVideoMode.Composite)
        {
            srcTex = _signal_tex_2;
        }
        else
        {
            srcTex = _signal_tex_1;
        }

        if (_config.CrtPreset != DreamboxCrtPreset.None)
        {
            // set up render state
            SDL.SDL_BindGPUGraphicsPipeline(renderPass, _pso_blit_tv.handle);
            SDL.SDL_BindGPUVertexBuffers(renderPass, 0, [new () {
                buffer = _screenBlitQuad.handle,
                offset = 0
            }], 1);
            SDL.SDL_BindGPUFragmentSamplers(renderPass, 0, [
                new () {
                    texture = srcTex.handle,
                    sampler = _linearSampler
                },
                new () {
                    texture = _shadowmask.handle,
                    sampler = _shadowmaskSampler
                }
            ], 2);

            BlitTVParams ubo = new BlitTVParams();

            switch (_config.CrtPreset)
            {
                case DreamboxCrtPreset.Curve:
                    ubo.curvature = new Vector2(0.025f, 0.025f);
                    break;
                case DreamboxCrtPreset.Flat:
                    ubo.curvature = Vector2.Zero;
                    break;
                case DreamboxCrtPreset.Trinitron:
                    ubo.curvature = new Vector2(0f, 0.025f);
                    break;
            }

            ubo.scale = new Vector2(
                1.0f / (1.0f - ubo.curvature.X),
                1.0f / (1.0f - ubo.curvature.Y));

            ubo.maskScale = new Vector2(
                640.0f / 6.0f,
                480.0f / 8.0f
            );

            unsafe
            {
                SDL.SDL_PushGPUFragmentUniformData(cmdBuf, 0, (nint)(&ubo), (uint)Unsafe.SizeOf<BlitTVParams>());
            }
        }
        else
        {
            // set up render state
            SDL.SDL_BindGPUGraphicsPipeline(renderPass, _pso_blit.handle);
            SDL.SDL_BindGPUVertexBuffers(renderPass, 0, [new () {
                buffer = _screenBlitQuad.handle,
                offset = 0
            }], 1);
            SDL.SDL_BindGPUFragmentSamplers(renderPass, 0, [
                new () {
                    texture = srcTex.handle,
                    sampler = _linearSampler
                },
            ], 1);
        }

        // draw quad
        SDL.SDL_DrawGPUPrimitives(renderPass, 6, 1, 0, 0);
    }

    public void GetTextures(List<VdpTextureHandle> outTextures)
    {
        for (int i = 0; i < _texCache.Count; i++)
        {
            var tex = _texCache[i];
            if (tex != null)
            {
                outTextures.Add(new VdpTextureHandle() {
                    handle = i,
                    texture = tex
                });
            }
        }
    }
    
    public void Dispose()
    {
        foreach (var tex in _texCache)
        {
            tex?.Dispose();
        }
        _texCache.Clear();

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

        if (_depthQueryFence != 0)
        {
            SDL.SDL_ReleaseGPUFence(_graphicsDevice.handle, _depthQueryFence);
        }

        SDL.SDL_ReleaseGPUSampler(_graphicsDevice.handle, _linearSampler);
        SDL.SDL_ReleaseGPUSampler(_graphicsDevice.handle, _shadowmaskSampler);

        _screenBlitQuad.Dispose();

        _convertYuvPipeline.Dispose();
        _yuvBuffer.Dispose();

        _depthQueryPipeline.Dispose();
        _depthQueryResults.Dispose();

        _vu_data.Dispose();
        _vu_program.Dispose();

        _pso_blit.Dispose();
        _pso_blit_tv.Dispose();
        _pso_blit_interlace.Dispose();
        _pso_gen_phase.Dispose();
        _pso_gen_signal.Dispose();
        _pso_dec_signal_1.Dispose();
        _pso_dec_signal_2.Dispose();
        _pso_sharpen.Dispose();

        _screenTarget0.Dispose();
        _screenTarget1.Dispose();
        _depthTarget.Dispose();
        _blankTexture.Dispose();
        
        _cb_lut.Dispose();
        _signal_tex_1.Dispose();
        _signal_tex_2.Dispose();
        
        _shadowmask.Dispose();

        _blit_vertex.Dispose();
        _blit_fragment.Dispose();
        _blit_tv.Dispose();
        _blit_interlace_fragment.Dispose();
        _vu_vertex.Dispose();
        _ff_fragment.Dispose();
        _gen_phase.Dispose();
        _gen_signal.Dispose();
        _dec_signal_1.Dispose();
        _dec_signal_2.Dispose();
        _sharpen.Dispose();
    }

    private static uint EncodeVUInstr(ushort opcode, ushort d, ushort s, ushort sx = 0, ushort sy = 0, ushort sz = 0, ushort sw = 0, ushort m = 0)
    {
        return (uint)(
            (opcode & 0xF) |
            ((d & 0xF) << 4) |
            ((s & 0xF) << 8) |
            ((sx & 3) << 12) |
            ((sy & 3) << 14) |
            ((sz & 3) << 16) |
            ((sw & 3) << 18) |
            ((m & 0xF) << 20));
    }
}