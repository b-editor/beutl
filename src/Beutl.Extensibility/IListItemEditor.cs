using System.ComponentModel;

using Avalonia.Controls;

namespace Beutl.Extensibility;

public interface IListItemEditor : INotifyPropertyChanged
{
    event EventHandler? DeleteRequested;

    Control? ReorderHandle { get; }
}
