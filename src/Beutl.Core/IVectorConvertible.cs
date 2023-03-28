using System.Numerics;

namespace Beutl;

public interface ITupleConvertible<TSelf, T>
    where TSelf : struct
    where T : unmanaged, INumber<T>
{
    static abstract int TupleLength { get; }

    static abstract void ConvertTo(TSelf self, Span<T> tuple);
    
    static abstract void ConvertFrom(Span<T> tuple, out TSelf self);
}
