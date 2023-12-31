namespace Beutl;

internal sealed class RingStack<T>(int size)
{
    private T[] _buffer = new T[4];
    private int _top = -1;

    public bool IsEmpty => Count == 0;

    public bool IsFull => Count == size;

    public int Count { get; private set; } = 0;

    private void Resize(int newCapacity)
    {
        Array.Resize(ref _buffer, newCapacity);

        _top = Count; // 先頭位置を変更
        Count++;
    }

    public void Push(T item)
    {
        if (Count == _buffer.Length
            && _buffer.Length < size) // 配列が満杯の場合
        {
            int newCapacity = _buffer.Length * 2; // 新しい容量は現在の2倍にする
            Resize(Math.Min(newCapacity, size));
        }
        else // 配列に空きがある場合
        {
            Count++;
            _top = (_top + 1) % _buffer.Length;
        }

        _buffer[_top] = item; // 新しいアイテムを追加
    }

    public T Pop()
    {
        if (Count == 0)
        {
            throw new InvalidOperationException("Stack underflow");
        }
        T item = _buffer[_top];
        _buffer[_top] = default!;

        _top = (_top - 1 + _buffer.Length) % _buffer.Length;
        Count--;
        return item;
    }

    public T Peek()
    {
        if (Count == 0)
        {
            throw new InvalidOperationException("Stack is empty");
        }
        return _buffer[_top];
    }

    public void Clear()
    {
        Array.Clear(_buffer);
        _top = -1;
        Count = 0;
    }
}
