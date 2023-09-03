namespace Beutl.Views;

internal readonly struct Cache<T>
    where T : class
{
    public readonly T?[] Items;

    public Cache(int size)
    {
        Items = new T?[size];
    }

    public bool Set(T item)
    {
        foreach (ref T? item0 in Items.AsSpan())
        {
            if (item0 == null)
            {
                item0 = item;
                return true;
            }
        }

        return false;
    }

    public T? Get()
    {
        foreach (ref T? item in Items.AsSpan())
        {
            if (item != null)
            {
                T? tmp = item;
                item = null;
                return tmp;
            }
        }

        return null;
    }
}
