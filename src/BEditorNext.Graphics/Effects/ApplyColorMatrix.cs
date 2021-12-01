using BEditorNext.Graphics.Pixel;

namespace BEditorNext.Graphics.Effects;

public class ApplyColorMatrix : IPixelEffect
{
    public ColorMatrix Matrix { get; set; }

    public void Apply(ref Bgra8888 pixel, BitmapInfo info, int index)
    {
        var vec = new ColorVector(pixel.R / 255F, pixel.G / 255F, pixel.B / 255F, pixel.A / 255F);

        vec *= Matrix;

        pixel = new Bgra8888(
            (byte)(vec.R * 255),
            (byte)(vec.G * 255),
            (byte)(vec.B * 255),
            (byte)(vec.A * 255));
    }
}
