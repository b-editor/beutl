using System.Collections.Specialized;
using System.ComponentModel;

namespace Beutl.Collections;

public interface ICoreReadOnlyList<out T> : IReadOnlyList<T>, INotifyCollectionChanged, INotifyPropertyChanged
{
}
