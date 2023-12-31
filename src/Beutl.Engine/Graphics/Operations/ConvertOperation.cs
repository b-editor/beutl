using Beutl.Media.Pixel;

namespace Beutl.Graphics.Operations;

public readonly unsafe struct ConvertOperation<T1, T2>(T1* src, T2* dst)
    where T1 : unmanaged, IPixel<T1>
    where T2 : unmanaged, IPixel<T2>
{
    public readonly void Invoke(int p)
    {
        var color = src[p].ToColor();
        dst[p] = default(T2).FromColor(color);
    }
}
