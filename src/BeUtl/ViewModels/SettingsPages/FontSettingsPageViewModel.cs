using System.Collections.ObjectModel;

using BeUtl.Configuration;

using Reactive.Bindings;

namespace BeUtl.ViewModels.SettingsPages;

public sealed class FontSettingsPageViewModel
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
