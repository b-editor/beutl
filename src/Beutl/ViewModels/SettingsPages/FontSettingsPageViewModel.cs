using System.Collections.ObjectModel;

using Beutl.Configuration;
using Beutl.Controls.Navigation;

using Reactive.Bindings;

namespace Beutl.ViewModels.SettingsPages;

public sealed class FontSettingsPageViewModel : PageContext, IDisposable
{
    public FontSettingsPageViewModel()
    {
        FontDirectories = GlobalConfiguration.Instance.FontConfig.FontDirectories;

        Remove.Subscribe(str => FontDirectories.Remove(str));
    }

    public ObservableCollection<string> FontDirectories { get; }

    public ReactiveCommand<string> Remove { get; } = new();

    public void Dispose()
    {
        Remove.Dispose();
    }
}
