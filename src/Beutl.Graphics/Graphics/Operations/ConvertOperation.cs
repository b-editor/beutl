using Beutl.Media.Pixel;

namespace Beutl.Graphics.Operations;

public readonly unsafe struct ConvertOperation<T1, T2>
    where T1 : unmanaged, IPixel<T1>
    where T2 : unmanaged, IPixel<T2>
{
    private readonly T1* _src;
    private readonly T2* _dst;

    public ConvertOperation(T1* src, T2* dst)
    {
        _src = src;
        _dst = dst;
    }

    public readonly void Invoke(int p)
    {
        var color = _src[p].ToColor();
        _dst[p] = default(T2).FromColor(color);
    }
}
