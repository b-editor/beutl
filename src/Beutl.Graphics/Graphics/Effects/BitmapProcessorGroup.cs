using Beutl.Media;
using Beutl.Media.Pixel;

namespace Beutl.Graphics.Effects;

internal sealed class BitmapProcessorGroup : IBitmapProcessor
{
    public IBitmapProcessor[] Processors { get; set; } = Array.Empty<IBitmapProcessor>();

    public void Process(in Bitmap<Bgra8888> src, out Bitmap<Bgra8888> dst)
    {
        Bitmap<Bgra8888> cur = src;
        Bitmap<Bgra8888>? tmp = null;
        foreach (IBitmapProcessor item in Processors.AsSpan())
        {
            item.Process(cur, out tmp);

            if (cur != src && cur != tmp)
            {
                cur.Dispose();
            }

            cur = tmp;
        }

        dst = tmp ?? cur;
    }
}
