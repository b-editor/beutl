namespace Beutl;

internal sealed class CommandStack<T>(int size)
{
    private readonly LinkedList<T> _buffer = new();

    public bool IsEmpty => Count == 0;

    public bool IsFull => Count == size;

    public int Count => _buffer.Count;

    public event Action<T>? Removed;

    public void Push(T item)
    {
        _buffer.AddFirst(item);

        while (_buffer.Count > size)
        {
            if (_buffer.Last is { Value: T lastValue })
            {
                _buffer.RemoveLast();
                Removed?.Invoke(lastValue);
            }
        }
    }

    public T? Pop()
    {
        if (_buffer.First is { Value: T lastValue })
        {
            _buffer.RemoveFirst();
            return lastValue;
        }

        return default;
    }

    public T? Peek()
    {
        if (_buffer.First is { Value: T lastValue })
        {
            return lastValue;
        }

        return default;
    }

    public void Clear()
    {
        _buffer.Clear();
    }
}
