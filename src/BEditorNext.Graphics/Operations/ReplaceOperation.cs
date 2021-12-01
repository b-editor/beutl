using BEditorNext.Graphics.Pixel;

namespace BEditorNext.Graphics.Operations;

public readonly unsafe struct ReplaceOperation<TPixel>
    where TPixel : unmanaged, IPixel<TPixel>
{
    private readonly Bitmap<TPixel> _src;
    private readonly Bitmap<TPixel> _dst;
    private readonly PixelRect _roi;

    public ReplaceOperation(Bitmap<TPixel> src, Bitmap<TPixel> dst, PixelRect roi)
    {
        _src = src;
        _dst = dst;
        _roi = roi;
    }

    public readonly void Invoke(int y)
    {
        var sourceRow = _src[y];
        var targetRow = _dst[y + _roi.Y].Slice(_roi.X, _roi.Width);

        sourceRow.CopyTo(targetRow);
    }
}
