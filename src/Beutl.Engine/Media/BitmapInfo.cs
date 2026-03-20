namespace Beutl.Media;

public readonly struct BitmapInfo(
    int width, int height, int byteCount, int bytesPerPixel,
    BitmapColorType colorType, BitmapAlphaType alphaType, BitmapColorSpace colorSpace)
{
    public int Width { get; } = width;

    public int Height { get; } = height;

    public int ByteCount { get; } = byteCount;

    public int BytesPerPixel { get; } = bytesPerPixel;

    public BitmapColorType ColorType { get; } = colorType;

    public BitmapAlphaType AlphaType { get; } = alphaType;

    public BitmapColorSpace ColorSpace { get; } = colorSpace;
}
