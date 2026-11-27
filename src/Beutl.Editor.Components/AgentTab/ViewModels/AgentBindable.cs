using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Beutl.Editor.Components.AgentTab.ViewModels;

internal abstract class AgentBindable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        Notify(name);
        return true;
    }

    protected void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected void Notify(string first, string second, params ReadOnlySpan<string> rest)
    {
        if (PropertyChanged is not { } handler)
            return;
        handler(this, new PropertyChangedEventArgs(first));
        handler(this, new PropertyChangedEventArgs(second));
        foreach (string name in rest)
            handler(this, new PropertyChangedEventArgs(name));
    }
}
