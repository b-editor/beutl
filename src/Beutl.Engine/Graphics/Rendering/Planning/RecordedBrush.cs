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
    int DependencyIndex)
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

internal readonly record struct ResolvedBrush(
    Brush.Resource? Resource,
    BrushTileContent? TileContent)
{
    public static ResolvedBrush Empty => default;
}

internal readonly record struct ResolvedPen(
    Pen.Resource? Resource,
    ResolvedBrush Brush)
{
    public static ResolvedPen Empty => default;
}

internal sealed record BrushTileContent(
    SKShader Shader,
    Rect Bounds,
    EffectiveScale EffectiveScale);
