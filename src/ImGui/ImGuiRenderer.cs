using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using DreamboxVM.Graphics;
using ImGuiNET;
using SDL3;

namespace DreamboxVM.ImGuiRendering;

using ImGui = ImGuiNET.ImGui;

class ImGuiRenderer : IDisposable
{
    private struct ImGuiDrawUbo
    {
        public Matrix4x4 proj;
    }

    private struct ImGuiVert : IVertex
    {
        public Vector2 pos;
        public Vector2 uv;
        public uint col;

        public static SDL.SDL_GPUVertexAttribute[] GetLayout()
        {
            return [
                new () {
                    location = 0,
                    buffer_slot = 0,
                    format = SDL.SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT2,
                    offset = 0,
                },
                new () {
                    location = 1,
                    buffer_slot = 0,
                    format = SDL.SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT2,
                    offset = 8,
                },
                new () {
                    location = 2,
                    buffer_slot = 0,
                    format = SDL.SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_UBYTE4_NORM,
                    offset = 16,
                }
            ];
        }
    }

    private GraphicsDevice _graphicsDevice;
    private VertexBuffer<ImGuiVert>? _vertexBuffer;
    private IndexBuffer<ushort>? _indexBuffer;

    private GraphicsPipeline _pso;
    private Shader _vertex;
    private Shader _fragment;
    private nint _linearSampler;

    private Dictionary<IntPtr, Texture> _textureCache = [];

    private int _textureId;
    private IntPtr? _fontTextureId;

    public static unsafe void HandleEvent(in SDL.SDL_Event evt)
    {
        switch((SDL.SDL_EventType)evt.type)
        {
            case SDL.SDL_EventType.SDL_EVENT_MOUSE_MOTION: {
                ImGui.GetIO().AddMousePosEvent(evt.motion.x, evt.motion.y);
                break;
            }
            case SDL.SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN: {
                ImGui.GetIO().AddMouseButtonEvent(evt.button.button - 1, true);
                break;
            }
            case SDL.SDL_EventType.SDL_EVENT_MOUSE_BUTTON_UP: {
                ImGui.GetIO().AddMouseButtonEvent(evt.button.button - 1, false);
                break;
            }
            case SDL.SDL_EventType.SDL_EVENT_MOUSE_WHEEL: {
                ImGui.GetIO().AddMouseWheelEvent(evt.wheel.x, evt.wheel.y);
                break;
            }
            case SDL.SDL_EventType.SDL_EVENT_KEY_DOWN: {
                if (TryMapKey((SDL.SDL_Keycode)evt.key.key, out var key))
                {
                    ImGui.GetIO().AddKeyEvent(key, true);
                }
                break;
            }
            case SDL.SDL_EventType.SDL_EVENT_KEY_UP: {
                if (TryMapKey((SDL.SDL_Keycode)evt.key.key, out var key))
                {
                    ImGui.GetIO().AddKeyEvent(key, false);
                }
                break;
            }
            case SDL.SDL_EventType.SDL_EVENT_TEXT_INPUT: {
                ImGui.GetIO().AddInputCharactersUTF8(Marshal.PtrToStringUTF8((IntPtr)evt.text.text));
                break;
            }
        }
    }

    public ImGuiRenderer(GraphicsDevice graphicsDevice, SDL.SDL_GPUTextureFormat targetFormat)
    {
        _graphicsDevice = graphicsDevice;

        var context = ImGui.CreateContext();
        ImGui.SetCurrentContext(context);

        // load shaders
        _vertex = LoadShader(_graphicsDevice, "content/shaders/imgui.vert.spv", SDL.SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_VERTEX, 0, 0, 1);
        _fragment = LoadShader(_graphicsDevice, "content/shaders/imgui.frag.spv", SDL.SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_FRAGMENT, 1, 0, 0);

        // create pso
        _pso = GraphicsPipeline.Create<ImGuiVert>(_graphicsDevice,
            [new () {
                format = targetFormat,
                blend_state = new ()
                {
                    src_color_blendfactor = SDL.SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_SRC_ALPHA,
                    src_alpha_blendfactor = SDL.SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_SRC_ALPHA,
                    dst_color_blendfactor = SDL.SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE_MINUS_SRC_ALPHA,
                    dst_alpha_blendfactor = SDL.SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE_MINUS_SRC_ALPHA,
                    color_blend_op = SDL.SDL_GPUBlendOp.SDL_GPU_BLENDOP_ADD,
                    alpha_blend_op = SDL.SDL_GPUBlendOp.SDL_GPU_BLENDOP_ADD,
                    enable_blend = true,
                    enable_color_write_mask = false,
                }
            }], SDL.SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_INVALID, false, _vertex, _fragment,
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

        _linearSampler = SDL.SDL_CreateGPUSampler(_graphicsDevice.handle, new SDL.SDL_GPUSamplerCreateInfo() {
            min_filter = SDL.SDL_GPUFilter.SDL_GPU_FILTER_LINEAR,
            mag_filter = SDL.SDL_GPUFilter.SDL_GPU_FILTER_LINEAR,
            address_mode_u = SDL.SDL_GPUSamplerAddressMode.SDL_GPU_SAMPLERADDRESSMODE_CLAMP_TO_EDGE,
            address_mode_v = SDL.SDL_GPUSamplerAddressMode.SDL_GPU_SAMPLERADDRESSMODE_CLAMP_TO_EDGE,
        });
    }

    public void Dispose()
    {
        _vertex.Dispose();
        _fragment.Dispose();
        _pso.Dispose();
        _vertexBuffer?.Dispose();
        _indexBuffer?.Dispose();

        foreach (var kv in _textureCache)
        {
            kv.Value.Dispose();
        }

        SDL.SDL_ReleaseGPUSampler(_graphicsDevice.handle, _linearSampler);
    }

    public void BeforeLayout(float dt, uint targetWidth, uint targetHeight)
    {
        ImGui.GetIO().DisplaySize = new Vector2(targetWidth, targetHeight);
        ImGui.GetIO().DisplayFramebufferScale = new Vector2(1f, 1f);
        ImGui.NewFrame();
    }

    public void AfterLayout(nint cmdBuf, nint renderPass, uint targetWidth, uint targetHeight)
    {
        ImGui.Render();
        unsafe
        {
            RenderDrawData(ImGui.GetDrawData(), cmdBuf, renderPass, targetWidth, targetHeight);
        }
        ImGui.EndFrame();
    }

    public unsafe void RebuildFontAtlas()
    {
        var io = ImGui.GetIO();
        io.Fonts.GetTexDataAsRGBA32(out byte* pixels, out int width, out int height);

        var tex = new Texture(_graphicsDevice, SDL.SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_R8G8B8A8_UNORM, width, height, 1,
            SDL.SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER);

        Span<byte> pxData = new (pixels, width * height * 4);

        var cmdBuf = SDL.SDL_AcquireGPUCommandBuffer(_graphicsDevice.handle);
        var copyPass = SDL.SDL_BeginGPUCopyPass(cmdBuf);
        tex.SetData(copyPass, cmdBuf, pxData, 0, 0, 0, 0, 0, width, height, 1, false);
        SDL.SDL_EndGPUCopyPass(copyPass);
        SDL.SDL_SubmitGPUCommandBuffer(cmdBuf);

        if (_fontTextureId.HasValue)
        {
            UnbindTexture(_fontTextureId.Value, true);
        }

        _fontTextureId = BindTexture(tex);

        io.Fonts.SetTexID(_fontTextureId.Value);
        io.Fonts.ClearTexData();
    }

    public IntPtr BindTexture(Texture texture)
    {
        var id = new IntPtr(_textureId++);
        _textureCache.Add(id, texture);
        return id;
    }

    public void UnbindTexture(IntPtr textureId, bool dispose = false)
    {
        if (dispose)
        {
            _textureCache[textureId].Dispose();
        }
        _textureCache.Remove(textureId);
    }

    private void RenderDrawData(ImDrawDataPtr drawData, nint cmdBuf, nint renderPass, uint targetWidth, uint targetHeight)
    {
        drawData.ScaleClipRects(ImGui.GetIO().DisplayFramebufferScale);
        UpdateBuffers(drawData);
        RenderCommandLists(drawData, cmdBuf, renderPass, targetWidth, targetHeight);
    }

    private unsafe void UpdateBuffers(ImDrawDataPtr drawData)
    {
        if (drawData.TotalVtxCount == 0) return;

        // expand buffers if we need more room
        if (_vertexBuffer == null || drawData.TotalVtxCount > _vertexBuffer.vertexCount)
        {
            _vertexBuffer?.Dispose();
            _vertexBuffer = new VertexBuffer<ImGuiVert>(_graphicsDevice, drawData.TotalVtxCount);
        }

        if (_indexBuffer == null || drawData.TotalIdxCount > _indexBuffer.indexCount)
        {
            _indexBuffer?.Dispose();
            _indexBuffer = new IndexBuffer<ushort>(_graphicsDevice, drawData.TotalIdxCount);
        }

        nint cmdBuf = SDL.SDL_AcquireGPUCommandBuffer(_graphicsDevice.handle);
        nint copyPass = SDL.SDL_BeginGPUCopyPass(cmdBuf);

        // upload data to buffers
        _vertexBuffer.BeginSetData(true);
        _indexBuffer.BeginSetData(true);
        {
            int vtxOffset = 0;
            int idxOffset = 0;

            for (int i = 0; i < drawData.CmdListsCount; i++)
            {
                ImDrawListPtr cmdList = drawData.CmdLists[i];

                Span<ImGuiVert> srcVertex = new ((void*)cmdList.VtxBuffer.Data, cmdList.VtxBuffer.Size);
                Span<ushort> srcIndex = new ((void*)cmdList.IdxBuffer.Data, cmdList.IdxBuffer.Size);

                _vertexBuffer.QueueSetData(copyPass, srcVertex, vtxOffset * Unsafe.SizeOf<ImGuiVert>());
                _indexBuffer.QueueSetData(copyPass, srcIndex, idxOffset * sizeof(ushort));

                vtxOffset += cmdList.VtxBuffer.Size;
                idxOffset += cmdList.IdxBuffer.Size;
            }
        }
        _vertexBuffer.EndSetData();
        _indexBuffer.EndSetData();

        SDL.SDL_EndGPUCopyPass(copyPass);
        SDL.SDL_SubmitGPUCommandBuffer(cmdBuf);
    }

    private unsafe void RenderCommandLists(ImDrawDataPtr drawData, nint cmdBuf, nint renderPass, uint targetWidth, uint targetHeight)
    {
        if (drawData.TotalVtxCount == 0) return;

        SDL.SDL_BindGPUGraphicsPipeline(renderPass, _pso.handle);
        SDL.SDL_SetGPUViewport(renderPass, new () {
            x = 0,
            y = 0,
            w = targetWidth,
            h = targetHeight,
            min_depth = 0.0f,
            max_depth = 1.0f                    
        });
        
        SDL.SDL_BindGPUVertexBuffers(renderPass, 0, [new () {
            buffer = _vertexBuffer!.handle,
            offset = 0
        }], 1);

        SDL.SDL_BindGPUIndexBuffer(renderPass, new () {
            buffer = _indexBuffer!.handle,
            offset = 0
        }, SDL.SDL_GPUIndexElementSize.SDL_GPU_INDEXELEMENTSIZE_16BIT);

        unsafe
        {
            var uboData = new ImGuiDrawUbo()
            {
                proj = Matrix4x4.Transpose(Matrix4x4.CreateOrthographicOffCenter(0f, targetWidth, targetHeight, 0f, -1f, 1f))
            };
            
            SDL.SDL_PushGPUVertexUniformData(cmdBuf, 0, (nint)(&uboData), (uint)Unsafe.SizeOf<ImGuiDrawUbo>());
        }

        int vtxOffset = 0;
        int idxOffset = 0;

        for (int i = 0; i < drawData.CmdListsCount; i++)
        {
            var cmdList = drawData.CmdLists[i];

            for (int cmd = 0; cmd < cmdList.CmdBuffer.Size; cmd++)
            {
                var drawCmd = cmdList.CmdBuffer[cmd];
                var tex = _textureCache[drawCmd.TextureId];

                SDL.SDL_SetGPUScissor(renderPass, new () {
                    x = (int)drawCmd.ClipRect.X,
                    y = (int)drawCmd.ClipRect.Y,
                    w = (int)(drawCmd.ClipRect.Z - drawCmd.ClipRect.X),
                    h = (int)(drawCmd.ClipRect.W - drawCmd.ClipRect.Y)
                });

                SDL.SDL_BindGPUFragmentSamplers(renderPass, 0, [
                    new () {
                        texture = tex.handle,
                        sampler = _linearSampler
                    }
                ], 1);

                SDL.SDL_DrawGPUIndexedPrimitives(renderPass,
                    drawCmd.ElemCount, 1, drawCmd.IdxOffset + (uint)idxOffset, (int)drawCmd.VtxOffset + vtxOffset, 0);
            }

            vtxOffset += cmdList.VtxBuffer.Size;
            idxOffset += cmdList.IdxBuffer.Size;
        }
    }

    private static bool TryMapKey(SDL.SDL_Keycode keycode, out ImGuiKey imguiKey)
    {
        imguiKey = keycode switch
        {
            SDL.SDL_Keycode.SDLK_BACKSPACE => ImGuiKey.Backspace,
            SDL.SDL_Keycode.SDLK_TAB => ImGuiKey.Tab,
            SDL.SDL_Keycode.SDLK_RETURN => ImGuiKey.Enter,
            SDL.SDL_Keycode.SDLK_CAPSLOCK => ImGuiKey.CapsLock,
            SDL.SDL_Keycode.SDLK_ESCAPE => ImGuiKey.Escape,
            SDL.SDL_Keycode.SDLK_SPACE => ImGuiKey.Space,
            SDL.SDL_Keycode.SDLK_PAGEUP => ImGuiKey.PageUp,
            SDL.SDL_Keycode.SDLK_PAGEDOWN => ImGuiKey.PageDown,
            SDL.SDL_Keycode.SDLK_END => ImGuiKey.End,
            SDL.SDL_Keycode.SDLK_HOME => ImGuiKey.Home,
            SDL.SDL_Keycode.SDLK_LEFT => ImGuiKey.LeftArrow,
            SDL.SDL_Keycode.SDLK_RIGHT => ImGuiKey.RightArrow,
            SDL.SDL_Keycode.SDLK_UP => ImGuiKey.UpArrow,
            SDL.SDL_Keycode.SDLK_DOWN => ImGuiKey.DownArrow,
            SDL.SDL_Keycode.SDLK_PRINTSCREEN => ImGuiKey.PrintScreen,
            SDL.SDL_Keycode.SDLK_INSERT => ImGuiKey.Insert,
            SDL.SDL_Keycode.SDLK_DELETE => ImGuiKey.Delete,
            SDL.SDL_Keycode.SDLK_0 => ImGuiKey._0,
            >= SDL.SDL_Keycode.SDLK_1 and <= SDL.SDL_Keycode.SDLK_9 => (ImGuiKey)((uint)ImGuiKey._1 + (keycode - SDL.SDL_Keycode.SDLK_1)),
            >= SDL.SDL_Keycode.SDLK_A and <= SDL.SDL_Keycode.SDLK_Z => (ImGuiKey)((uint)ImGuiKey.A + (keycode - SDL.SDL_Keycode.SDLK_A)),
            SDL.SDL_Keycode.SDLK_KP_0 => ImGuiKey.Keypad0,
            >= SDL.SDL_Keycode.SDLK_KP_1 and <= SDL.SDL_Keycode.SDLK_KP_9 => (ImGuiKey)((uint)ImGuiKey.Keypad1 + (keycode - SDL.SDL_Keycode.SDLK_KP_1)),
            SDL.SDL_Keycode.SDLK_KP_MULTIPLY => ImGuiKey.KeypadMultiply,
            SDL.SDL_Keycode.SDLK_KP_PLUS => ImGuiKey.KeypadAdd,
            SDL.SDL_Keycode.SDLK_KP_MINUS => ImGuiKey.KeypadSubtract,
            SDL.SDL_Keycode.SDLK_KP_PERIOD => ImGuiKey.KeypadDecimal,
            SDL.SDL_Keycode.SDLK_KP_DIVIDE => ImGuiKey.KeypadDivide,
            >= SDL.SDL_Keycode.SDLK_F1 and <= SDL.SDL_Keycode.SDLK_F12 => (ImGuiKey)((uint)ImGuiKey.F1 + (keycode - SDL.SDL_Keycode.SDLK_F1)),
            SDL.SDL_Keycode.SDLK_NUMLOCKCLEAR => ImGuiKey.NumLock,
            SDL.SDL_Keycode.SDLK_SCROLLLOCK => ImGuiKey.ScrollLock,
            SDL.SDL_Keycode.SDLK_LSHIFT => ImGuiKey.ModShift,
            SDL.SDL_Keycode.SDLK_LCTRL => ImGuiKey.ModCtrl,
            SDL.SDL_Keycode.SDLK_LALT => ImGuiKey.ModAlt,
            SDL.SDL_Keycode.SDLK_SEMICOLON => ImGuiKey.Semicolon,
            SDL.SDL_Keycode.SDLK_EQUALS => ImGuiKey.Equal,
            SDL.SDL_Keycode.SDLK_COMMA => ImGuiKey.Comma,
            SDL.SDL_Keycode.SDLK_MINUS => ImGuiKey.Minus,
            SDL.SDL_Keycode.SDLK_PERIOD => ImGuiKey.Period,
            SDL.SDL_Keycode.SDLK_QUESTION => ImGuiKey.Slash,
            SDL.SDL_Keycode.SDLK_TILDE => ImGuiKey.GraveAccent,
            SDL.SDL_Keycode.SDLK_LEFTBRACKET => ImGuiKey.LeftBracket,
            SDL.SDL_Keycode.SDLK_RIGHTBRACKET => ImGuiKey.RightBracket,
            SDL.SDL_Keycode.SDLK_BACKSLASH => ImGuiKey.Backslash,
            SDL.SDL_Keycode.SDLK_APOSTROPHE => ImGuiKey.Apostrophe,
            SDL.SDL_Keycode.SDLK_AC_BACK => ImGuiKey.AppBack,
            SDL.SDL_Keycode.SDLK_AC_FORWARD => ImGuiKey.AppForward,
            _ => ImGuiKey.None
        };

        return imguiKey != ImGuiKey.None;
    }

    private static Shader LoadShader(GraphicsDevice device, string path, SDL.SDL_GPUShaderStage stage,
        int numSamplers, int numStorageBuffers, int numUniformBuffers)
    {
        byte[] data = File.ReadAllBytes(path);
        return new Shader(device, data, "main", stage, numSamplers, numStorageBuffers, numUniformBuffers);
    }
}