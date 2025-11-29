using Beutl.Media;

namespace Beutl.Graphics;

internal readonly struct TileBrushCalculator
{
    private readonly Size _imageSize;
    private readonly Rect _drawRect;

    public TileBrushCalculator(TileBrush.Resource brush, Size contentSize, Size targetSize)
        : this(
              brush.TileMode,
              brush.Stretch,
              brush.AlignmentX,
              brush.AlignmentY,
              brush.SourceRect,
              brush.DestinationRect,
              contentSize,
              targetSize)
    {
    }

    public TileBrushCalculator(
        TileMode tileMode,
        Stretch stretch,
        AlignmentX alignmentX,
        AlignmentY alignmentY,
        RelativeRect sourceRect,
        RelativeRect destinationRect,
        Size contentSize,
        Size targetSize)
    {
        _imageSize = contentSize;

        SourceRect = sourceRect.ToPixels(_imageSize);
        DestinationRect = destinationRect.ToPixels(targetSize);

        Vector scale = stretch.CalculateScaling(DestinationRect.Size, SourceRect.Size);
        Vector translate = CalculateTranslate(alignmentX, alignmentY, SourceRect, DestinationRect, scale);

        IntermediateSize = tileMode == TileMode.None ? targetSize : DestinationRect.Size;
        IntermediateTransform = CalculateIntermediateTransform(
            tileMode,
            SourceRect,
            DestinationRect,
            scale,
            translate,
            out _drawRect);
    }

    public Rect DestinationRect { get; }

    public Rect IntermediateClip => _drawRect;

    public Size IntermediateSize { get; }

    public Matrix IntermediateTransform { get; }

    public bool NeedsIntermediate
    {
        get
        {
            if (IntermediateTransform != Matrix.Identity)
                return true;
            if (SourceRect.Position != default)
                return true;
            if (SourceRect.Size.AspectRatio == _imageSize.AspectRatio)
                return false;
            if (SourceRect.Width != _imageSize.Width ||
                SourceRect.Height != _imageSize.Height)
                return true;
            return false;
        }
    }

    public Rect SourceRect { get; }

    public static Vector CalculateTranslate(
        AlignmentX alignmentX,
        AlignmentY alignmentY,
        Rect sourceRect,
        Rect destinationRect,
        Vector scale)
    {
        float x = 0.0f;
        float y = 0.0f;
        Size size = sourceRect.Size * scale;

        switch (alignmentX)
        {
            case AlignmentX.Center:
                x += (destinationRect.Width - size.Width) / 2;
                break;
            case AlignmentX.Right:
                x += destinationRect.Width - size.Width;
                break;
        }

        switch (alignmentY)
        {
            case AlignmentY.Center:
                y += (destinationRect.Height - size.Height) / 2;
                break;
            case AlignmentY.Bottom:
                y += destinationRect.Height - size.Height;
                break;
        }

        return new Vector(x, y);
    }

    public static Matrix CalculateIntermediateTransform(
        TileMode tileMode,
        Rect sourceRect,
        Rect destinationRect,
        Vector scale,
        Vector translate,
        out Rect drawRect)
    {
        Matrix transform = Matrix.CreateTranslation(-sourceRect.Position) *
                           Matrix.CreateScale(scale) *
                           Matrix.CreateTranslation(translate);
        Rect dr;

        if (tileMode == TileMode.None)
        {
            dr = destinationRect;
            transform *= Matrix.CreateTranslation(destinationRect.Position);
        }
        else
        {
            dr = new Rect(destinationRect.Size);
        }

        drawRect = dr;

        return transform;
    }
}
