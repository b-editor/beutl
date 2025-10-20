using System.Collections;

namespace Beutl.Collections;

public interface ICoreList : IList
{
    new int Count { get; }

    void Move(int oldIndex, int newIndex);

    void MoveRange(int oldIndex, int count, int newIndex);

    void RemoveRange(int index, int count);

    new void RemoveAt(int index);

    new void Clear();
}

public interface ICoreList<T> : IObservableList<T>, ICoreReadOnlyList<T>, ICoreList
{
    new bool IsReadOnly { get; }

    new int Count { get; }

    new T this[int index] { get; set; }

    public event Action<T>? Attached;

    public event Action<T>? Detached;

    [Obsolete("Use 'GetMarshal'.")]
    Span<T> AsSpan();

    CoreListMarshal<T> GetMarshal();

    void AddRange(IEnumerable<T> items);

    void InsertRange(int index, IEnumerable<T> items);

    void Replace(IList<T> source);

    new void Move(int oldIndex, int newIndex);

    new void MoveRange(int oldIndex, int count, int newIndex);

    new void RemoveRange(int index, int count);

    new void RemoveAt(int index);

    new void Clear();
}
