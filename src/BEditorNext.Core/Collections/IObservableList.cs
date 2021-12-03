using System.Collections.Specialized;

namespace BEditorNext.Collections;

public interface IObservableList<T> : IList<T>, INotifyCollectionChanged
{
}
