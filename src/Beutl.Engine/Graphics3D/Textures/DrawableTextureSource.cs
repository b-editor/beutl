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
        private readonly TextureCache _frameCache = new();
        private readonly TextureCache _auxiliaryCache = new();

        public override ITexture2D? GetTexture(
            IGraphicsContext graphicsContext,
            RenderIntent renderIntent,
            RenderPullPurpose pullPurpose,
            float surfaceDensity = 1f)
        {
            renderIntent = RenderPolicyValidation.Validate(renderIntent, nameof(renderIntent));
            pullPurpose = RenderPolicyValidation.Validate(pullPurpose, nameof(pullPurpose));
            if (Drawable == null)
            {
                _frameCache.Dispose();
                _auxiliaryCache.Dispose();
                return null;
            }

            TextureCache cache = pullPurpose == RenderPullPurpose.Auxiliary
                ? _auxiliaryCache
                : _frameCache;

            // Rasterize at surfaceDensity so vector content stays crisp.
            int textureWidth = TextureWidth;
            int textureHeight = TextureHeight;
            float density = float.IsFinite(surfaceDensity) && surfaceDensity > 0f ? surfaceDensity : 1f;
            density = RenderNodeContext.ClampWorkingScaleToBufferBudget(
                new Rect(0, 0, textureWidth, textureHeight), density);
            int deviceWidth = Math.Max(1, (int)Math.Ceiling(textureWidth * (double)density));
            int deviceHeight = Math.Max(1, (int)Math.Ceiling(textureHeight * (double)density));

            if (cache.Width != deviceWidth || cache.Height != deviceHeight || cache.RenderTarget == null)
            {
                cache.DisposeRenderTarget();

                cache.RenderTarget = RenderTarget.Create(deviceWidth, deviceHeight);
                if (cache.RenderTarget == null)
                {
                    if (renderIntent == RenderIntent.Delivery)
                    {
                        throw new InvalidOperationException(
                            $"Drawable texture allocation failed ({deviceWidth}x{deviceHeight} px, density {density}).");
                    }

                    return null;
                }

                cache.Width = deviceWidth;
                cache.Height = deviceHeight;
                cache.RenderTargetVersion = -1; // force a re-render into the resized target
            }

            // Re-render on content change or density change.
            if (cache.RenderTargetVersion != Version || cache.Density != density)
            {
                cache.Density = density;
                cache.DrawableNode ??= new DrawableRenderNode(Drawable);
                cache.DrawableNode.Update(Drawable);
                using (var context = new GraphicsContext2D(
                           cache.DrawableNode, new Size(textureWidth, textureHeight), density))
                {
                    Drawable.GetOriginal().Render(context, Drawable);
                }

                var processor = new RenderNodeProcessor(
                    cache.DrawableNode, true, renderIntent, density, density, pullPurpose: pullPurpose);
                using (var canvas = new ImmediateCanvas(
                           cache.RenderTarget, renderIntent, density, density, pullPurpose: pullPurpose))
                {
                    canvas.Clear();
                    processor.Render(canvas);
                }

                // Prepare for sampling (flush the surface)
                cache.RenderTarget.PrepareForSampling();
                cache.RenderTargetVersion = Version;
            }

            return cache.RenderTarget?.Texture;
        }

        partial void PostDispose(bool disposing)
        {
            _frameCache.Dispose();
            _auxiliaryCache.Dispose();
        }

        private sealed class TextureCache : IDisposable
        {
            public DrawableRenderNode? DrawableNode { get; set; }

            public RenderTarget? RenderTarget { get; set; }

            public int RenderTargetVersion { get; set; } = -1;

            public int Width { get; set; }

            public int Height { get; set; }

            public float Density { get; set; } = -1f;

            public void DisposeRenderTarget()
            {
                RenderTarget?.Dispose();
                RenderTarget = null;
            }

            public void Dispose()
            {
                DisposeRenderTarget();
                DrawableNode?.Dispose();
                DrawableNode = null;
                RenderTargetVersion = -1;
                Width = 0;
                Height = 0;
                Density = -1f;
            }
        }
    }
}
