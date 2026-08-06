using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Rendering;

internal enum RecordedBrushKind : byte
{
    Empty,
    Declarative,
    Drawable,
    RawExternal,
}

internal sealed record RecordedBrush(
    RecordedBrushKind Kind,
    RenderResource<Brush.Resource>? Resource,
    int DependencyIndex,
    Rect? ContentBoundsHint = null)
{
    public static RecordedBrush Empty { get; } = new(RecordedBrushKind.Empty, null, -1);

    public bool HasDependency => DependencyIndex >= 0;

    public bool IsRawExternal => Kind == RecordedBrushKind.RawExternal;
}

internal sealed record RecordedPen(
    RenderResource<Pen.Resource>? Resource,
    RecordedBrush Brush)
{
    public static RecordedPen Empty { get; } = new(null, RecordedBrush.Empty);
}

internal sealed class RecordedBrushPlan(
    RecordedBrush brush,
    IReadOnlyList<RenderFragmentHandle> dependencies,
    IReadOnlyList<RenderResource> resources)
{
    public RecordedBrush Brush { get; } = brush;

    public IReadOnlyList<RenderFragmentHandle> Dependencies { get; } = dependencies;

    public IReadOnlyList<RenderResource> Resources { get; } = resources;

    public bool IsRawExternal => Brush.IsRawExternal;

    public bool CanBeUsedAsValueInput
        => !IsRawExternal
           && Dependencies.All(static dependency => dependency.CanBeUsedAsValueInput);
}

internal sealed class RecordedPaint(
    RecordedBrush fill,
    RecordedPen pen,
    IReadOnlyList<RenderFragmentHandle> dependencies,
    IReadOnlyList<RenderResource> resources)
{
    public RecordedBrush Fill { get; } = fill;

    public RecordedPen Pen { get; } = pen;

    public IReadOnlyList<RenderFragmentHandle> Dependencies { get; } = dependencies;

    public IReadOnlyList<RenderResource> Resources { get; } = resources;

    public bool HasRawExternalWork => Fill.IsRawExternal || Pen.Brush.IsRawExternal;
}

internal sealed record BrushTileContent(
    SKShader Shader,
    Rect Bounds,
    EffectiveScale EffectiveScale);
