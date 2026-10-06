using Microsoft.Extensions.ObjectPool;

namespace Beutl.Graphics.Effects;

internal sealed class ArrayPooledObjectPolicy<T>(int length) : IPooledObjectPolicy<T[]>
{
    public T[] Create()
    {
        return new T[length];
    }

    public bool Return(T[] obj)
    {
        Array.Clear(obj);
        return true;
    }
}
