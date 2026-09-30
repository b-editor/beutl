using Beutl.Graphics;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Requests;

namespace Beutl.Graphics3D.Textures;

/// <summary>
/// Renders a list of 2D drawables laid out on a 2D canvas and exposes the part they cover as a texture.
/// </summary>
/// <remarks>
/// The drawables belong to the flow they were taken from, so this never disposes them.
/// </remarks>
internal sealed class DrawableContentTexture : TextureSource.Resource, IRecordedTextureSource
{
    private const float EdgeMargin = 2;

    private readonly ContainerRenderNode _root = new();

    public IReadOnlyList<Drawable.Resource> Drawables { get; set; } = [];

    /// <summary>The 2D canvas the drawables are laid out on, as they would be in a 2D scene of that size.</summary>
    public Size CanvasSize { get; private set; }

    /// <summary>The part of the canvas the drawables cover, clipped to the canvas.</summary>
    public Rect ContentBounds { get; private set; }

    public Rect TextureDomain => ContentBounds;

    public bool HasContent => ContentBounds.Width > 0 && ContentBounds.Height > 0;

    /// <summary>Lays the drawables out on a canvas of <paramref name="canvasSize"/> and measures what they cover.</summary>
    /// <param name="canvasSize">The logical size of the 2D canvas.</param>
    /// <param name="density">The density the scene is rendered at, whose pixel grid the bounds are aligned to.</param>
    public void UpdateLayout(Size canvasSize, float density)
    {
        CanvasSize = canvasSize;
        if (Drawables.Count == 0 || canvasSize.Width <= 0 || canvasSize.Height <= 0)
        {
            ContentBounds = default;
            return;
        }

        var canvas = new Rect(canvasSize);
        RenderNode root = Record(1f);
        using var renderer = new RenderNodeRenderer(
            root,
            new RenderNodeRenderRequest
            {
                Intent = RenderIntent.Preview,
                TargetDomain = canvas,
            });
        Rect bounds = renderer.Measure().OutputBounds;
        float sanitizedDensity = RenderScaleUtilities.SanitizeOutputScale(density);
        // Anti-aliasing and glyph overshoot reach past the measured bounds; a 2D render keeps them, so the
        // card keeps a margin of device pixels for them too.
        ContentBounds = !bounds.IsInvalid && bounds.Width > 0 && bounds.Height > 0
            ? PixelAlign(bounds.Inflate(EdgeMargin / sanitizedDensity).Intersect(canvas), sanitizedDensity)
            : default;
    }

    /// <summary>Whether the drawables cover <paramref name="canvasPoint"/>, as a 2D hit test would answer.</summary>
    public bool HitTest(Point canvasPoint)
    {
        if (!HasContent)
            return false;

        RenderNode root = Record(1f);
        using var renderer = new RenderNodeRenderer(
            root,
            new RenderNodeRenderRequest
            {
                Intent = RenderIntent.Preview,
                TargetDomain = new Rect(CanvasSize),
            });
        return renderer.HitTest(canvasPoint);
    }

    public float ResolveDensity(float density)
    {
        float sanitizedDensity = RenderScaleUtilities.SanitizeOutputScale(density);
        return BufferDimensionBudget.Resolve(BufferBudgetScope.Allocation).ClampWorkingScale(
            TextureDomain,
            sanitizedDensity);
    }

    public RenderNode? RecordContent(float density)
    {
        return HasContent ? Record(ResolveDensity(density)) : null;
    }

    public override ITexture2D? GetTexture(IGraphicsContext graphicsContext, float surfaceDensity = 1f)
    {
        return HasContent && NestedRenderTargetBindingScope.TryGet(this, out NestedRenderTargetBinding binding)
            ? binding.GetTexture(TextureDomain, ResolveDensity(surfaceDensity))
            : null;
    }

    private ContainerRenderNode Record(float density)
    {
        using var context = new GraphicsContext2D(_root, CanvasSize, density);
        foreach (Drawable.Resource drawable in Drawables)
        {
            context.DrawDrawable(drawable);
        }

        return _root;
    }

    // Whole device pixels keep the texture's texels on the scene's pixel grid, so an unmoved card samples one
    // texel per pixel and shows the drawables as sharply as a 2D render of them.
    private static Rect PixelAlign(Rect rect, float density)
    {
        float left = MathF.Floor(rect.Left * density) / density;
        float top = MathF.Floor(rect.Top * density) / density;
        float right = MathF.Ceiling(rect.Right * density) / density;
        float bottom = MathF.Ceiling(rect.Bottom * density) / density;
        return new Rect(left, top, right - left, bottom - top);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _root.Dispose();
        }

        base.Dispose(disposing);
    }
}
