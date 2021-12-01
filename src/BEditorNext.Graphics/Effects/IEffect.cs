using BEditorNext.Graphics.Pixel;

namespace BEditorNext.Graphics.Effects;

public interface IEffect
{
    public Bitmap<Bgra8888> Apply(Bitmap<Bgra8888> bitmap);
}

public unsafe interface IPixelEffect : IEffect
{
    public void Apply(ref Bgra8888 pixel, BitmapInfo info, int index);

    Bitmap<Bgra8888> IEffect.Apply(Bitmap<Bgra8888> bitmap)
    {
        Parallel.For(0, bitmap.Width * bitmap.Height, pos =>
        {
            var ptr = (Bgra8888*)bitmap.Data;
            Apply(ref ptr[pos], bitmap.Info, pos);
        });

        return bitmap;
    }
}

public unsafe interface IRowEffect : IEffect
{
    public void Apply(Span<Bgra8888> pixel, BitmapInfo info, int row);

    Bitmap<Bgra8888> IEffect.Apply(Bitmap<Bgra8888> bitmap)
    {
        Parallel.For(0, bitmap.Height, pos =>
        {
            var span = bitmap.DataSpan[(pos * bitmap.Width)..];
            Apply(span, bitmap.Info, pos);
        });

        return bitmap;
    }
}
