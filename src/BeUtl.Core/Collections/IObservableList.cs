using System.Collections.Specialized;

namespace BeUtl.Collections;

public interface IObservableList<T> : IList<T>, INotifyCollectionChanged
{
}
