using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics3D.Textures;

[Display(Name = nameof(GraphicsStrings.DrawableTextureSource), ResourceType = typeof(GraphicsStrings))]
public sealed partial class DrawableTextureSource : TextureSource
{
    public DrawableTextureSource()
    {
        ScanProperties<DrawableTextureSource>();
    }

    [Display(Name = nameof(GraphicsStrings.Drawable), ResourceType = typeof(GraphicsStrings))]
    public IProperty<Drawable?> Drawable { get; } = Property.Create<Drawable?>(null);

    [Display(Name = nameof(GraphicsStrings.DrawableTextureSource_TextureWidth), ResourceType = typeof(GraphicsStrings))]
    [Range(1, 8192)]
    public IProperty<int> TextureWidth { get; } = Property.CreateAnimatable(256);

    [Display(Name = nameof(GraphicsStrings.DrawableTextureSource_TextureHeight), ResourceType = typeof(GraphicsStrings))]
    [Range(1, 8192)]
    public IProperty<int> TextureHeight { get; } = Property.CreateAnimatable(256);

    public partial class Resource
    {
        private DrawableRenderNode? _drawableNode;
        private RenderTarget? _renderTarget;
        private int _renderTargetVersion = -1;
        private int _lastWidth;
        private int _lastHeight;
        private float _lastDensity = -1f;

        public override ITexture2D? GetTexture(IGraphicsContext graphicsContext, float surfaceDensity = 1f)
        {
            if (Drawable == null)
            {
                DisposeRenderTarget();
                return null;
            }

            // Rasterize at surfaceDensity so vector content stays crisp.
            int textureWidth = TextureWidth;
            int textureHeight = TextureHeight;
            float density = float.IsFinite(surfaceDensity) && surfaceDensity > 0f ? surfaceDensity : 1f;
            density = RenderNodeContext.ClampWorkingScaleToBufferBudget(
                new Rect(0, 0, textureWidth, textureHeight), density);
            int deviceWidth = Math.Max(1, (int)Math.Ceiling(textureWidth * (double)density));
            int deviceHeight = Math.Max(1, (int)Math.Ceiling(textureHeight * (double)density));

            if (_lastWidth != deviceWidth || _lastHeight != deviceHeight || _renderTarget == null)
            {
                DisposeRenderTarget();

                _renderTarget = RenderTarget.Create(deviceWidth, deviceHeight);
                if (_renderTarget == null) return null;

                _lastWidth = deviceWidth;
                _lastHeight = deviceHeight;
                _renderTargetVersion = -1; // force a re-render into the resized target
            }

            // Re-render on content change or density change.
            if (_renderTargetVersion != Version || _lastDensity != density)
            {
                _lastDensity = density;
                _drawableNode ??= new DrawableRenderNode(Drawable);
                _drawableNode.Update(Drawable);
                using (var context = new GraphicsContext2D(
                           _drawableNode, new Size(textureWidth, textureHeight), density))
                {
                    Drawable.GetOriginal().Render(context, Drawable);
                }

                var processor = new RenderNodeProcessor(_drawableNode, true, density, density);
                using (var canvas = new ImmediateCanvas(_renderTarget, density, density))
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
