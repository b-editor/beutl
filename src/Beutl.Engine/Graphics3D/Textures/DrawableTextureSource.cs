using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Requests;
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
    public IExpressionProperty<Drawable?> Drawable { get; } = Property.CreateExpression<Drawable?>(null);

    [Display(Name = nameof(GraphicsStrings.DrawableTextureSource_TextureWidth), ResourceType = typeof(GraphicsStrings))]
    [Range(1, 8192)]
    public IExpressionProperty<int> TextureWidth { get; } = Property.CreateAnimatable(256);

    [Display(Name = nameof(GraphicsStrings.DrawableTextureSource_TextureHeight), ResourceType = typeof(GraphicsStrings))]
    [Range(1, 8192)]
    public IExpressionProperty<int> TextureHeight { get; } = Property.CreateAnimatable(256);

    public partial class Resource : IRecordedTextureSource
    {
        private DrawableRenderNode? _drawableNode;

        internal Rect TextureDomain
            => new(0, 0, TextureWidth, TextureHeight);

        internal float ResolveDensity(float density)
        {
            return RecordedTextureDensity.Resolve(TextureDomain, density);
        }

        Rect IRecordedTextureSource.TextureDomain => TextureDomain;

        float IRecordedTextureSource.ResolveDensity(float density) => ResolveDensity(density);

        RenderNode? IRecordedTextureSource.RecordContent(float density) => RecordDrawable(density);

        internal DrawableRenderNode? RecordDrawable(float density)
        {
            if (Drawable is null || TextureWidth <= 0 || TextureHeight <= 0)
                return null;

            float sanitizedDensity = ResolveDensity(density);
            _drawableNode ??= new DrawableRenderNode(Drawable);
            _drawableNode.Update(Drawable);
            using var context = new GraphicsContext2D(
                _drawableNode,
                new Size(TextureWidth, TextureHeight),
                sanitizedDensity);
            Drawable.Render(context);
            return _drawableNode;
        }

        public override ITexture2D? GetTexture(IGraphicsContext graphicsContext, float surfaceDensity = 1f)
        {
            ArgumentNullException.ThrowIfNull(graphicsContext);
            if (Drawable is null)
            {
                return null;
            }

            if (NestedRenderTargetBindingScope.TryGet(this, out NestedRenderTargetBinding nestedBinding))
                return nestedBinding.GetTexture(TextureDomain, ResolveDensity(surfaceDensity));
            if (RenderExecutionCallbackGuard.IsActive)
            {
                throw new InvalidOperationException(
                    "A drawable texture used by a deferred render callback has no prepared nested target.");
            }

            return null;
        }

        partial void PostDispose(bool disposing)
        {
            _drawableNode?.Dispose();
            _drawableNode = null;
        }
    }
}
