using System.Collections.Specialized;
using System.ComponentModel;

namespace BeUtl.Collections;

public interface ICoreReadOnlyList<out T> : IReadOnlyList<T>, INotifyCollectionChanged, INotifyPropertyChanged
{
}
