
using Beutl.Configuration;

using Reactive.Bindings;

namespace Beutl.ViewModels.SettingsPages;

public sealed class ViewSettingsPageViewModel
{
    private readonly ViewConfig _config;

    public ViewSettingsPageViewModel()
    {
        _config = GlobalConfiguration.Instance.ViewConfig;
        SelectedTheme = _config.GetObservable(ViewConfig.ThemeProperty).Select(x => (int)x)
            .ToReactiveProperty();
        SelectedTheme.Subscribe(v => _config.Theme = (ViewConfig.ViewTheme)v);

        SelectedLanguage = _config.GetObservable(ViewConfig.UICultureProperty)
            .ToReactiveProperty()!;

        SelectedLanguage.Subscribe(ci => _config.UICulture = ci);
    }

    public ReactiveProperty<int> SelectedTheme { get; }

    public ReactiveProperty<CultureInfo> SelectedLanguage { get; }

    public IEnumerable<CultureInfo> Cultures { get; } = LocalizeService.Instance.SupportedCultures();
}
