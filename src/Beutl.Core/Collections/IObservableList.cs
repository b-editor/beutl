using System.Collections.Specialized;
using System.ComponentModel;

namespace Beutl.Collections;

public interface IObservableList<T> : IList<T>, INotifyCollectionChanged, INotifyPropertyChanged
{
}
