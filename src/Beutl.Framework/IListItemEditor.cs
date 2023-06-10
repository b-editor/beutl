using System.ComponentModel;

using Avalonia.Controls;

namespace Beutl.Framework;

public interface IListItemEditor : INotifyPropertyChanged
{
    event EventHandler? DeleteRequested;

    Control? ReorderHandle { get; }
}
