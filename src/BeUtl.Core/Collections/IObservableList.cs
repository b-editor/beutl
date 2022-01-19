using System.Collections.Specialized;
using System.ComponentModel;

namespace BeUtl.Collections;

public interface IObservableList<T> : IList<T>, INotifyCollectionChanged, INotifyPropertyChanged
{
}
