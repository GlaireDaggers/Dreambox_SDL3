using System.Numerics;
using DreamboxVM.ImGuiRendering;
using DreamboxVM.VM;
using ImGuiNET;

namespace DreamboxVM.Windows;

class TextureViewerWindow : WindowBase
{
    private readonly DreamboxApp _app;
    private readonly ImGuiRenderer _imGuiRenderer;

    private List<VdpTextureHandle> _textures = [];
    private string[] _textureNames = [];

    private int _selectedTexture = 0;

    private nint _curTextureId;
    private VdpTexture? _curTexture;
    
    public TextureViewerWindow(DreamboxApp app, ImGuiRenderer imGuiRenderer) : base("Texture Viewer", new Vector2(400, 250))
    {
        _app = app;
        _imGuiRenderer = imGuiRenderer;
    }

    protected override void DrawContents()
    {
        base.DrawContents();

        // gather list of textures
        _textures.Clear();
        _app.VdpInstance.GetTextures(_textures);

        if (_textureNames.Length < _textures.Capacity)
        {
            Array.Resize(ref _textureNames, _textures.Capacity);
        }

        Array.Fill(_textureNames, "");
        for (int i = 0; i < _textures.Count; i++)
        {
            _textureNames[i] = $"Texture {_textures[i].handle} ({_textures[i].texture.texture.width}x{_textures[i].texture.texture.height})";
        }

        ImGui.Combo("Textures", ref _selectedTexture, _textureNames, _textures.Count);

        if (_textures.Count > 0)
        {
            var tex = _textures[_selectedTexture];

            if (tex.texture != _curTexture)
            {
                if (_curTexture != null)
                {
                    _imGuiRenderer.UnbindTexture(_curTextureId);
                }
                _curTextureId = _imGuiRenderer.BindTexture(tex.texture.texture);
                _curTexture = tex.texture;
            }

            // texture info
            ImGui.Text($"Format: {tex.texture.format}");
            ImGui.Text($"Dimensions: {tex.texture.texture.width} x {tex.texture.texture.height}");
            ImGui.Text($"Mip levels: {tex.texture.texture.levels}");

            bool isRenderTex = tex.texture is VdpRenderTexture;
            ImGui.Text($"Render texture: {(isRenderTex ? "true" : "false")}");

            ImGui.Text($"Total Size (bytes): {tex.texture.sizeBytes}");

            // draw texture
            if (ImGui.BeginChild("##imgscroll", Vector2.Zero, ImGuiChildFlags.None, ImGuiWindowFlags.HorizontalScrollbar))
            {
                ImGui.Image(_curTextureId, new Vector2(_curTexture.texture.width, _curTexture.texture.height));
                ImGui.EndChild();
            }
        }
        else
        {
            if (_curTexture != null)
            {
                _imGuiRenderer.UnbindTexture(_curTextureId);
                _curTexture = null;
            }
        }
    }
}