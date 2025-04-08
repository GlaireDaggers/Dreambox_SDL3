namespace DreamboxVM.Graphics;

using System.Numerics;
using System.Runtime.CompilerServices;
using DreamboxVM;
using DreamboxVM.VM;
using SDL3;

class ScreenEffects : IDisposable
{
    const int COLORBURST_LENGTH = 3;
    const float COLORBURST_PPS = 0.25f;
    const float COLORBURST_PPF = 0.25f;
    const float SIGNAL_NOISE = 0.01f;

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

    private readonly GraphicsDevice _graphicsDevice;

    private DreamboxConfig _config;

    private readonly Texture _screenTarget0;
    private readonly Texture _screenTarget1;
    private readonly Texture _screenTargetCombine;
    
    private readonly Texture _shadowmask;

    private readonly Texture _cb_lut;         // NTSC colorburst phase lookup
    private readonly Texture _signal_tex_1;   // used to generate/decode NTSC signal
    private readonly Texture _signal_tex_2;

    private bool _screenFlip = false;

    private Shader _blit_vertex;
    private Shader _blit_fragment;
    private Shader _blit_tv;
    private Shader _blit_interlace_fragment;

    private Shader _gen_phase;
    private Shader _gen_signal;
    private Shader _dec_signal_1;
    private Shader _dec_signal_2;
    private Shader _sharpen;

    private GraphicsPipeline _pso_blit;
    private GraphicsPipeline _pso_blit_screen;
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

    private int _curFrame = 0;

    public ScreenEffects(GraphicsDevice graphicsDevice, DreamboxConfig config)
    {
        _graphicsDevice = graphicsDevice;
        _config = config;

        _screenTarget0 = new Texture(graphicsDevice, SDL.SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_R8G8B8A8_UNORM, VDP.SCREEN_WIDTH, VDP.SCREEN_HEIGHT, 1,
            SDL.SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_COLOR_TARGET | SDL.SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER);
        _screenTarget1 = new Texture(graphicsDevice, SDL.SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_R8G8B8A8_UNORM, VDP.SCREEN_WIDTH, VDP.SCREEN_HEIGHT, 1,
            SDL.SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_COLOR_TARGET | SDL.SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER);
        _screenTargetCombine = new Texture(graphicsDevice, SDL.SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_R8G8B8A8_UNORM, VDP.SCREEN_WIDTH, VDP.SCREEN_HEIGHT, 1,
            SDL.SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_COLOR_TARGET | SDL.SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER);

        _cb_lut = new Texture(graphicsDevice, SDL.SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_R16G16_FLOAT, 1, VDP.SCREEN_HEIGHT, 1,
            SDL.SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_COLOR_TARGET | SDL.SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER);

        _signal_tex_1 = new Texture(graphicsDevice, SDL.SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_R16G16B16A16_FLOAT, VDP.SCREEN_WIDTH * COLORBURST_LENGTH, VDP.SCREEN_HEIGHT, 1,
            SDL.SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_COLOR_TARGET | SDL.SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER);

        _signal_tex_2 = new Texture(graphicsDevice, SDL.SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_R16G16B16A16_FLOAT, VDP.SCREEN_WIDTH * COLORBURST_LENGTH, VDP.SCREEN_HEIGHT, 1,
            SDL.SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_COLOR_TARGET | SDL.SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER);

        // load shaders
        _blit_vertex = Shader.LoadShader(_graphicsDevice, "content/shaders/blit.vert.spv", SDL.SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_VERTEX, 0, 0, 0);
        _blit_fragment = Shader.LoadShader(_graphicsDevice, "content/shaders/blit.frag.spv", SDL.SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_FRAGMENT, 1, 0, 0);
        _blit_tv = Shader.LoadShader(_graphicsDevice, "content/shaders/blit_tv.frag.spv", SDL.SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_FRAGMENT, 2, 0, 1);
        _blit_interlace_fragment = Shader.LoadShader(_graphicsDevice, "content/shaders/blit_interlace.frag.spv", SDL.SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_FRAGMENT, 2, 0, 1);

        _gen_phase = Shader.LoadShader(_graphicsDevice, "content/shaders/gen_phase.frag.spv", SDL.SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_FRAGMENT, 0, 0, 1);
        _gen_signal = Shader.LoadShader(_graphicsDevice, "content/shaders/gen_signal.frag.spv", SDL.SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_FRAGMENT, 2, 0, 1);
        _dec_signal_1 = Shader.LoadShader(_graphicsDevice, "content/shaders/dec_signal_1.frag.spv", SDL.SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_FRAGMENT, 1, 0, 1);
        _dec_signal_2 = Shader.LoadShader(_graphicsDevice, "content/shaders/dec_signal_2.frag.spv", SDL.SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_FRAGMENT, 2, 0, 1);
        _sharpen = Shader.LoadShader(_graphicsDevice, "content/shaders/sharpen.frag.spv", SDL.SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_FRAGMENT, 1, 0, 1);

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

        _pso_blit_screen = GraphicsPipeline.Create<BlitVertex>(graphicsDevice,
            [new () {
                format = _graphicsDevice.swapchainFormat,
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

        // vertex buffers
        _screenBlitQuad = new VertexBuffer<BlitVertex>(_graphicsDevice, 6);
        _internalBlitQuad = new VertexBuffer<BlitVertex>(_graphicsDevice, 6);

        BlitVertex.SetBlitQuad(_graphicsDevice, _internalBlitQuad, 1.0f, 1.0f);

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
    }

    public void DirectBlit(nint renderPass, Texture inputTexture)
    {
        SDL.SDL_BindGPUGraphicsPipeline(renderPass, _pso_blit.handle);
        SDL.SDL_BindGPUVertexBuffers(renderPass, 0, [new () {
            buffer = _internalBlitQuad.handle,
            offset = 0
        }], 1);
        SDL.SDL_BindGPUFragmentSamplers(renderPass, 0, [
            new () {
                texture = inputTexture.handle,
                sampler = _linearSampler
            },
        ], 1);

        SDL.SDL_DrawGPUPrimitives(renderPass, 6, 1, 0, 0);
    }

    public void ReceiveFrame(nint cmdBuf, Texture inputTexture)
    {
        if (_config.InterlacedVideo)
        {
            Texture curTarget = _screenFlip ? _screenTarget1 : _screenTarget0;

            // blit input texture to alternating screenTarget0/screenTarget1
            nint renderPass = SDL.SDL_BeginGPURenderPass(cmdBuf, [
                    new SDL.SDL_GPUColorTargetInfo() {
                        texture = curTarget.handle,
                        load_op = SDL.SDL_GPULoadOp.SDL_GPU_LOADOP_DONT_CARE,
                        store_op = SDL.SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
                        cycle = true
                    }
                ], 1, Unsafe.NullRef<SDL.SDL_GPUDepthStencilTargetInfo>());
            {
                SDL.SDL_BindGPUGraphicsPipeline(renderPass, _pso_blit.handle);
                SDL.SDL_BindGPUVertexBuffers(renderPass, 0, [new () {
                    buffer = _internalBlitQuad.handle,
                    offset = 0
                }], 1);
                SDL.SDL_BindGPUFragmentSamplers(renderPass, 0, [
                    new () {
                        texture = inputTexture.handle,
                        sampler = _linearSampler
                    },
                ], 1);

                SDL.SDL_DrawGPUPrimitives(renderPass, 6, 1, 0, 0);
            }
            SDL.SDL_EndGPURenderPass(renderPass);

            // combine screenTarget0/screenTarget1 into single interlaced target
            renderPass = SDL.SDL_BeginGPURenderPass(cmdBuf, [
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
                    SDL.SDL_PushGPUFragmentUniformData(cmdBuf, 0, (nint)(&ubo), (uint)Unsafe.SizeOf<BlitInterlaceParams>());
                }

                // draw quad
                SDL.SDL_DrawGPUPrimitives(renderPass, 6, 1, 0, 0);
            }
            SDL.SDL_EndGPURenderPass(renderPass);
        }
        else
        {
            // todo: probably more efficient way to do this
            nint renderPass = SDL.SDL_BeginGPURenderPass(cmdBuf, [
                    new SDL.SDL_GPUColorTargetInfo() {
                        texture = _screenTargetCombine.handle,
                        load_op = SDL.SDL_GPULoadOp.SDL_GPU_LOADOP_DONT_CARE,
                        store_op = SDL.SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
                        cycle = true
                    }
                ], 1, Unsafe.NullRef<SDL.SDL_GPUDepthStencilTargetInfo>());
            {
                SDL.SDL_BindGPUGraphicsPipeline(renderPass, _pso_blit.handle);
                SDL.SDL_BindGPUVertexBuffers(renderPass, 0, [new () {
                    buffer = _internalBlitQuad.handle,
                    offset = 0
                }], 1);
                SDL.SDL_BindGPUFragmentSamplers(renderPass, 0, [
                    new () {
                        texture = inputTexture.handle,
                        sampler = _linearSampler
                    },
                ], 1);

                SDL.SDL_DrawGPUPrimitives(renderPass, 6, 1, 0, 0);
            }
            SDL.SDL_EndGPURenderPass(renderPass);
        }

        if (_config.VideoMode == DreamboxVideoMode.Composite || _config.VideoMode == DreamboxVideoMode.SVideo)
        {
            // generate colorburst phase LUT
            nint renderPass = SDL.SDL_BeginGPURenderPass(cmdBuf, [
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
                ubo.screenHeight = VDP.SCREEN_HEIGHT;
                ubo.ppf = COLORBURST_PPF;
                ubo.pps = COLORBURST_PPS;
                ubo.frame = _curFrame + 1;

                unsafe
                {
                    SDL.SDL_PushGPUFragmentUniformData(cmdBuf, 0, (nint)(&ubo), (uint)Unsafe.SizeOf<GenPhaseParams>());
                }

                // draw quad
                SDL.SDL_DrawGPUPrimitives(renderPass, 6, 1, 0, 0);
            }
            SDL.SDL_EndGPURenderPass(renderPass);

            // generate NTSC signal
            renderPass = SDL.SDL_BeginGPURenderPass(cmdBuf, [
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
                        texture = inputTexture.handle,
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
                    SDL.SDL_PushGPUFragmentUniformData(cmdBuf, 0, (nint)(&ubo), (uint)Unsafe.SizeOf<GenSignalParams>());
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
                renderPass = SDL.SDL_BeginGPURenderPass(cmdBuf, [
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
                        SDL.SDL_PushGPUFragmentUniformData(cmdBuf, 0, (nint)(&ubo), (uint)Unsafe.SizeOf<DecSignal1Params>());
                    }

                    // draw quad
                    SDL.SDL_DrawGPUPrimitives(renderPass, 6, 1, 0, 0);
                }
                SDL.SDL_EndGPURenderPass(renderPass);

                decodeSrc = _signal_tex_2;
                decodeDst = _signal_tex_1;
            }

            // decode NTSC signal (pass 2)
            renderPass = SDL.SDL_BeginGPURenderPass(cmdBuf, [
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
                    SDL.SDL_PushGPUFragmentUniformData(cmdBuf, 0, (nint)(&ubo), (uint)Unsafe.SizeOf<DecSignal2Params>());
                }

                // draw quad
                SDL.SDL_DrawGPUPrimitives(renderPass, 6, 1, 0, 0);
            }
            SDL.SDL_EndGPURenderPass(renderPass);

            // sharpen
            renderPass = SDL.SDL_BeginGPURenderPass(cmdBuf, [
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
                    SDL.SDL_PushGPUFragmentUniformData(cmdBuf, 0, (nint)(&ubo), (uint)Unsafe.SizeOf<SharpenParams>());
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
        BlitVertex.SetBlitQuad(_graphicsDevice, _screenBlitQuad, widthScale, heightScale);

        Texture srcTex;
        if (_config.VideoMode == DreamboxVideoMode.Default || _config.VideoMode == DreamboxVideoMode.VGA)
        {
            srcTex = _screenTargetCombine;
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
            SDL.SDL_BindGPUGraphicsPipeline(renderPass, _pso_blit_screen.handle);
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

    public void Dispose()
    {
        _screenTarget0.Dispose();
        _screenTarget1.Dispose();
        _screenTargetCombine.Dispose();

        _shadowmask.Dispose();

        _cb_lut.Dispose();
        _signal_tex_1.Dispose();
        _signal_tex_2.Dispose();

        _blit_vertex.Dispose();
        _blit_fragment.Dispose();
        _blit_tv.Dispose();
        _blit_interlace_fragment.Dispose();

        _gen_phase.Dispose();
        _gen_signal.Dispose();
        _dec_signal_1.Dispose();
        _dec_signal_2.Dispose();
        _sharpen.Dispose();

        _pso_blit.Dispose();
        _pso_blit_screen.Dispose();
        _pso_blit_tv.Dispose();
        _pso_blit_interlace.Dispose();
        _pso_gen_phase.Dispose();
        _pso_gen_signal.Dispose();
        _pso_dec_signal_1.Dispose();
        _pso_dec_signal_2.Dispose();
        _pso_sharpen.Dispose();

        _screenBlitQuad.Dispose();
        _internalBlitQuad.Dispose();

        SDL.SDL_ReleaseGPUSampler(_graphicsDevice.handle, _linearSampler);
        SDL.SDL_ReleaseGPUSampler(_graphicsDevice.handle, _shadowmaskSampler);
    }
}