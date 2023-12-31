using Beutl.Media.Pixel;

namespace Beutl.Graphics.Operations;

internal readonly unsafe struct AlphaMapOperation(Bgra8888* src, Grayscale8* dst)
{
    public void Invoke(int pos)
    {
        dst[pos] = new(src[pos].A);
    }
}
