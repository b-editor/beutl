
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

        HidePrimaryProperties = _config.GetObservable(ViewConfig.HidePrimaryPropertiesProperty).ToReactiveProperty();
        HidePrimaryProperties.Subscribe(b => _config.HidePrimaryProperties = b);

        PrimaryProperties = _config.PrimaryProperties;

        RemovePrimaryProperty.Subscribe(v => PrimaryProperties.Remove(v));
        ResetPrimaryProperty.Subscribe(_ => _config.ResetPrimaryProperties());
    }

    public ReactiveProperty<int> SelectedTheme { get; }

    public ReactiveProperty<CultureInfo> SelectedLanguage { get; }

    public IEnumerable<CultureInfo> Cultures { get; } = LocalizeService.Instance.SupportedCultures();

    public ReactiveProperty<bool> HidePrimaryProperties { get; }

    public CoreList<string> PrimaryProperties { get; }

    public ReactiveCommand<string> RemovePrimaryProperty { get; } = new();

    public ReactiveCommand ResetPrimaryProperty { get; } = new();
}
