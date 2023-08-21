using System.Collections.ObjectModel;

using Beutl.Configuration;
using Beutl.Controls.Navigation;

using Reactive.Bindings;

namespace Beutl.ViewModels.SettingsPages;
public sealed class FontSettingsPageViewModel : PageContext
{
    public FontSettingsPageViewModel()
    {
        FontDirectories = GlobalConfiguration.Instance.FontConfig.FontDirectories;
        IsSelected = SelectFont.Select(dir => dir is not null).ToReadOnlyReactiveProperty();

        Remove.Subscribe(str => FontDirectories.Remove(str));
    }

    public ReactiveProperty<string> SelectFont { get; } = new();

    public ObservableCollection<string> FontDirectories { get; }

    public ReadOnlyReactiveProperty<bool> IsSelected { get; }

    public ReactiveCommand<string> Remove { get; } = new();
}
