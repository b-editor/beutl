using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics3D.Textures;

[Display(Name = nameof(Strings.DrawableTextureSource), ResourceType = typeof(Strings))]
public sealed partial class DrawableTextureSource : TextureSource
{
    public DrawableTextureSource()
    {
        ScanProperties<DrawableTextureSource>();
    }

    [Display(Name = nameof(Strings.Drawable), ResourceType = typeof(Strings))]
    public IProperty<Drawable?> Drawable { get; } = Property.Create<Drawable?>(null);

    [Display(Name = nameof(Strings.TextureWidth), ResourceType = typeof(Strings))]
    [Range(1, 8192)]
    public IProperty<int> TextureWidth { get; } = Property.CreateAnimatable(256);

    [Display(Name = nameof(Strings.TextureHeight), ResourceType = typeof(Strings))]
    [Range(1, 8192)]
    public IProperty<int> TextureHeight { get; } = Property.CreateAnimatable(256);

    public partial class Resource
    {
        private DrawableRenderNode? _drawableNode;
        private RenderTarget? _renderTarget;
        private int _renderTargetVersion = -1;
        private int _lastWidth;
        private int _lastHeight;

        public override ITexture2D? GetTexture(IGraphicsContext graphicsContext)
        {
            if (Drawable == null)
            {
                DisposeRenderTarget();
                return null;
            }

            int textureWidth = TextureWidth;
            int textureHeight = TextureHeight;

            if (_lastWidth != textureWidth || _lastHeight != textureHeight || _renderTarget == null)
            {
                DisposeRenderTarget();

                _renderTarget = RenderTarget.Create(textureWidth, textureHeight);
                if (_renderTarget == null) return null;

                _lastWidth = textureWidth;
                _lastHeight = textureHeight;
            }

            if (_renderTargetVersion != Version)
            {
                _drawableNode ??= new DrawableRenderNode(Drawable);
                _drawableNode.Update(Drawable);
                using (var context = new GraphicsContext2D(_drawableNode, new PixelSize(textureWidth, textureHeight)))
                {
                    Drawable.GetOriginal().Render(context, Drawable);
                }

                var processor = new RenderNodeProcessor(_drawableNode, true);
                using (var canvas = new ImmediateCanvas(_renderTarget))
                {
                    canvas.Clear();
                    processor.Render(canvas);
                }

                // Prepare for sampling (flush the surface)
                _renderTarget.PrepareForSampling();
                _renderTargetVersion = Version;
            }

            return _renderTarget?.Texture;
        }

        private void DisposeRenderTarget()
        {
            _renderTarget?.Dispose();
            _renderTarget = null;
        }

        partial void PostDispose(bool disposing)
        {
            DisposeRenderTarget();
        }
    }
}
