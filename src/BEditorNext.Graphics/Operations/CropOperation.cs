using BEditorNext.Graphics.Pixel;

namespace BEditorNext.Graphics.Operations;

public readonly unsafe struct CropOperation<TPixel>
    where TPixel : unmanaged, IPixel<TPixel>
{
    private readonly Bitmap<TPixel> _src;
    private readonly Bitmap<TPixel> _dst;
    private readonly PixelRect _roi;

    public CropOperation(Bitmap<TPixel> src, Bitmap<TPixel> dst, PixelRect roi)
    {
        _src = src;
        _dst = dst;
        _roi = roi;
    }

    public readonly void Invoke(int y)
    {
        var sourceRow = _src[y + _roi.Y].Slice(_roi.X, _roi.Width);
        var targetRow = _dst[y];

        sourceRow.Slice(0, _roi.Width).CopyTo(targetRow);
    }
}
