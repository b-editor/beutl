using Beutl.Collections;
using Beutl.Collections.Pooled;
using Beutl.Media;

namespace Beutl.Graphics.Effects;

public readonly struct Contours(PooledList<PooledList<PixelPoint>> contours) : IDisposable
{
    public int Count => List.Count;

    public ReadOnlySpan<PixelPoint> this[int index] => List[index].Span;

    public PooledList<PooledList<PixelPoint>> List { get; } = contours;

    public PooledList<PooledList<PixelPoint>>.Enumerator GetEnumerator()
    {
        return List.GetEnumerator();
    }

    public void Dispose()
    {
        foreach (var contour in List)
        {
            contour.Dispose();
        }
        List.Dispose();
    }
}
