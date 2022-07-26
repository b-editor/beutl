using System.Collections;

namespace BeUtl.Collections;

public interface ICoreList<T> : IObservableList<T>, IList, ICoreReadOnlyList<T>
{
    new int Count { get; }

    new T this[int index] { get; set; }

    public event Action<T>? Attached;

    public event Action<T>? Detached;

    Span<T> AsSpan();

    void AddRange(IEnumerable<T> items);

    void InsertRange(int index, IEnumerable<T> items);

    void Move(int oldIndex, int newIndex);

    void MoveRange(int oldIndex, int count, int newIndex);

    void RemoveRange(int index, int count);

    new void RemoveAt(int index);

    new void Clear();
}
