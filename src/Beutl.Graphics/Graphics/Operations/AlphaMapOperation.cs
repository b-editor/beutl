using Beutl.Media.Pixel;

namespace Beutl.Graphics.Operations;

internal readonly unsafe struct AlphaMapOperation
{
    private readonly Bgra8888* _src;
    private readonly Grayscale8* _dst;

    public AlphaMapOperation(Bgra8888* src, Grayscale8* dst)
    {
        _src = src;
        _dst = dst;
    }

    public void Invoke(int pos)
    {
        _dst[pos] = new(_src[pos].A);
    }
}
