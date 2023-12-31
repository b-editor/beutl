using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.Rendering;

namespace Beutl.Graphics.Rendering;

internal sealed class RenderSceneBrush(IDrawableBrush @base, RenderScene? scene, Rect bounds)
    : IDrawableBrush, IEquatable<IDrawableBrush?>, IDisposable
{
    public RenderScene? Scene { get; private set; } = scene;

    public Rect Bounds { get; } = bounds;

    public IDrawableBrush Base { get; } = @base;

    public AlignmentX AlignmentX => Base.AlignmentX;

    public AlignmentY AlignmentY => Base.AlignmentY;

    public RelativeRect DestinationRect => Base.DestinationRect;

    public RelativeRect SourceRect => Base.SourceRect;

    public Stretch Stretch => Base.Stretch;

    public TileMode TileMode => Base.TileMode;

    public BitmapInterpolationMode BitmapInterpolationMode => Base.BitmapInterpolationMode;

    public float Opacity => Base.Opacity;

    public ITransform? Transform => Base.Transform;

    public RelativePoint TransformOrigin => Base.TransformOrigin;

    public Drawable? Drawable => Base.Drawable;

    public void Dispose()
    {
        Scene?.Dispose();
        Scene = null;
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as IDrawableBrush);
    }

    public bool Equals(IDrawableBrush? other)
    {
        return other is not null
            && AlignmentX == other.AlignmentX
            && AlignmentY == other.AlignmentY
            && DestinationRect.Equals(other.DestinationRect)
            && Opacity == other.Opacity
            && EqualityComparer<ITransform?>.Default.Equals(Transform, other.Transform)
            && TransformOrigin.Equals(other.TransformOrigin)
            && SourceRect.Equals(other.SourceRect)
            && Stretch == other.Stretch
            && TileMode == other.TileMode
            && BitmapInterpolationMode == other.BitmapInterpolationMode
            && ReferenceEquals(Drawable, other.Drawable)
            && Drawable?.Version == other.Drawable?.Version;
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(AlignmentX);
        hash.Add(AlignmentY);
        hash.Add(DestinationRect);
        hash.Add(Opacity);
        hash.Add(Transform);
        hash.Add(TransformOrigin);
        hash.Add(SourceRect);
        hash.Add(Stretch);
        hash.Add(TileMode);
        hash.Add(BitmapInterpolationMode);
        hash.Add(Drawable);
        return hash.ToHashCode();
    }
}
