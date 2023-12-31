using Avalonia.Media;

using Beutl.Configuration;
using Beutl.Controls.Navigation;

using FluentAvalonia.Styling;

using Reactive.Bindings;

namespace Beutl.ViewModels.SettingsPages;

public sealed class ViewSettingsPageViewModel : PageContext
{
    private readonly ViewConfig _config;
    // 一部の設定を移動したので、ナビゲーションするために
    private readonly Lazy<EditorSettingsPageViewModel> _editorSettings;

    public ViewSettingsPageViewModel(Lazy<EditorSettingsPageViewModel> editorSettings)
    {
        _config = GlobalConfiguration.Instance.ViewConfig;
        _editorSettings = editorSettings;

        SelectedTheme = _config.GetObservable(ViewConfig.ThemeProperty).Select(x => (int)x)
            .ToReactiveProperty();
        SelectedTheme.Subscribe(v => _config.Theme = (ViewConfig.ViewTheme)v);

        SelectedLanguage = _config.GetObservable(ViewConfig.UICultureProperty)
            .ToReactiveProperty()!;

        SelectedLanguage.Subscribe(ci => _config.UICulture = ci);

        GetPredefColors();

        bool result = Color.TryParse(_config.CustomAccentColor, out Color customColor);

        UseCustomAccent = new ReactiveProperty<bool>(_config.UseCustomAccentColor);
        ListBoxColor = new ReactiveProperty<Color?>(result ? customColor : null);
        CustomAccentColor = new ReactiveProperty<Color>(customColor);

        UseCustomAccent.Skip(1).Subscribe(value =>
        {
            if (value)
            {
                FluentAvaloniaTheme? faTheme = App.GetFATheme();
                if (faTheme?.TryGetResource("SystemAccentColor", null, out object? curColor) == true)
                {
                    CustomAccentColor.Value = (Color)curColor;
                }
                else
                {
                    Debug.Fail("Unable to retreive SystemAccentColor");
                }
            }
            else
            {
                // Restore system color
                CustomAccentColor.Value = default;
                ListBoxColor.Value = default;
                UpdateAppAccentColor(null);
            }
        });

        ListBoxColor.Skip(1).Subscribe(value =>
        {
            if (value != null)
            {
                CustomAccentColor.Value = value.Value;
                UpdateAppAccentColor(value.Value);
            }
        });

        CustomAccentColor.Skip(1).Subscribe(value =>
        {
            ListBoxColor.Value = value;
            UpdateAppAccentColor(value);
        });

        NavigateToEditorSettings = new AsyncReactiveCommand()
            .WithSubscribe(async () =>
            {
                INavigationProvider nav = await GetNavigation();
                await nav.NavigateAsync(
                    x => x is not null,
                    () => _editorSettings.Value);
            });
    }

    public ReactiveProperty<int> SelectedTheme { get; }

    public ReactiveProperty<CultureInfo> SelectedLanguage { get; }

    public IEnumerable<CultureInfo> Cultures { get; } = LocalizeService.Instance.SupportedCultures();

    public ReactiveProperty<bool> UseCustomAccent { get; }

    public ReactiveProperty<Color?> ListBoxColor { get; }

    public ReactiveProperty<Color> CustomAccentColor { get; }

    public IReadOnlyList<Color> PredefinedColors { get; } = GetPredefColors();

    public AsyncReactiveCommand NavigateToEditorSettings { get; }

    // https://github.com/amwx/FluentAvalonia/blob/master/samples/FAControlsGallery/ViewModels/SettingsPageViewModel.cs
    private static Color[] GetPredefColors()
    {
        return
        [
            Color.FromRgb(255,185,0),
            Color.FromRgb(255,140,0),
            Color.FromRgb(247,99,12),
            Color.FromRgb(202,80,16),
            Color.FromRgb(218,59,1),
            Color.FromRgb(239,105,80),
            Color.FromRgb(209,52,56),
            Color.FromRgb(255,67,67),
            Color.FromRgb(231,72,86),
            Color.FromRgb(232,17,35),
            Color.FromRgb(234,0,94),
            Color.FromRgb(195,0,82),
            Color.FromRgb(227,0,140),
            Color.FromRgb(191,0,119),
            Color.FromRgb(194,57,179),
            Color.FromRgb(154,0,137),
            Color.FromRgb(0,120,212),
            Color.FromRgb(0,99,177),
            Color.FromRgb(142,140,216),
            Color.FromRgb(107,105,214),
            Color.FromRgb(135,100,184),
            Color.FromRgb(116,77,169),
            Color.FromRgb(177,70,194),
            Color.FromRgb(136,23,152),
            Color.FromRgb(0,153,188),
            Color.FromRgb(45,125,154),
            Color.FromRgb(0,183,195),
            Color.FromRgb(3,131,135),
            Color.FromRgb(0,178,148),
            Color.FromRgb(1,133,116),
            Color.FromRgb(0,204,106),
            Color.FromRgb(16,137,62),
            Color.FromRgb(122,117,116),
            Color.FromRgb(93,90,88),
            Color.FromRgb(104,118,138),
            Color.FromRgb(81,92,107),
            Color.FromRgb(86,124,115),
            Color.FromRgb(72,104,96),
            Color.FromRgb(73,130,5),
            Color.FromRgb(16,124,16),
            Color.FromRgb(118,118,118),
            Color.FromRgb(76,74,72),
            Color.FromRgb(105,121,126),
            Color.FromRgb(74,84,89),
            Color.FromRgb(100,124,100),
            Color.FromRgb(82,94,84),
            Color.FromRgb(132,117,69),
            Color.FromRgb(126,115,95)
        ];
    }

    private static void UpdateAppAccentColor(Color? color)
    {
        ViewConfig viewConfig = GlobalConfiguration.Instance.ViewConfig;
        viewConfig.UseCustomAccentColor = color.HasValue;
        viewConfig.CustomAccentColor = color?.ToString();

        FluentAvaloniaTheme? faTheme = App.GetFATheme();
        if (faTheme != null)
        {
            faTheme.CustomAccentColor = color;
        }
    }
}
