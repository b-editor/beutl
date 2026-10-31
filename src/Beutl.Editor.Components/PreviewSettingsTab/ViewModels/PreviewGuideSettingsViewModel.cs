using Avalonia.Media;

using Beutl.Configuration;

using Reactive.Bindings;

namespace Beutl.Editor.Components.PreviewSettingsTab.ViewModels;

// The preview guides' settings, which the preview settings tab and the editor settings page both edit. EditorConfig
// stays the single source of truth, so the guide toggle and every open view agree.
public sealed class PreviewGuideSettingsViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = [];

    public PreviewGuideSettingsViewModel(EditorConfig config)
    {
        IsEnabled = SettingBinding.Bind(config.IsPreviewGuideEnabled, v => v, v => v, _disposables);

        Kinds =
        [
            new(config, PreviewGuideKinds.TitleSafe, Strings.PreviewGuide_TitleSafe),
            new(config, PreviewGuideKinds.ActionSafe, Strings.PreviewGuide_ActionSafe),
            new(config, PreviewGuideKinds.RuleOfThirds, Strings.PreviewGuide_RuleOfThirds),
            new(config, PreviewGuideKinds.CenterCross, Strings.PreviewGuide_CenterCross),
            new(config, PreviewGuideKinds.Diagonals, Strings.PreviewGuide_Diagonals),
            new(config, PreviewGuideKinds.CustomGrid, Strings.PreviewGuide_CustomGrid),
            new(config, PreviewGuideKinds.AspectRatio, Strings.PreviewGuide_AspectRatio),
        ];
        foreach (PreviewGuideKindItem item in Kinds)
        {
            item.DisposeWith(_disposables);
        }

        // The grid's and the aspect ratios' settings apply while their check boxes are checked.
        IsGridEnabled = Kinds.Single(item => item.Kind == PreviewGuideKinds.CustomGrid).IsChecked;
        IsAspectRatioEnabled = Kinds.Single(item => item.Kind == PreviewGuideKinds.AspectRatio).IsChecked;

        // Written as #AARRGGBB: Color.ToString would write a known color's name instead.
        Color = SettingBinding.Bind(config.PreviewGuideColor, ParseColor, color => $"#{color.ToUInt32():X8}", _disposables);
        Opacity = SettingBinding.Bind(config.PreviewGuideOpacity, v => (double)v, v => (float)v, _disposables);
        GridColumns = SettingBinding.Bind(config.PreviewGuideGridColumns, v => v, v => v, _disposables);
        GridRows = SettingBinding.Bind(config.PreviewGuideGridRows, v => v, v => v, _disposables);

        // Only a list that reads is saved; the text box keeps one that does not, with its error, while it is edited.
        AspectRatios = new ReactiveProperty<string>(config.PreviewGuideAspectRatios.Value)
            .SetValidateNotifyError(text => PreviewGuideAspectRatio.TryParseList(text, out _)
                ? null
                : Strings.PreviewGuide_AspectRatios_Invalid)
            .DisposeWith(_disposables);
        config.PreviewGuideAspectRatios.GetObservable()
            .Subscribe(text => AspectRatios.Value = text)
            .DisposeWith(_disposables);
        AspectRatios
            .Skip(1)
            .Where(text => PreviewGuideAspectRatio.TryParseList(text, out _))
            .Subscribe(text => config.PreviewGuideAspectRatios.Value = text ?? "")
            .DisposeWith(_disposables);

        // Presets can share their ratios, so the one picked stays shown while the list still matches it.
        SelectedPreset = new ReactiveProperty<PreviewGuideAspectRatioPreset?>(
                PreviewGuideAspectRatioPreset.Find(config.PreviewGuideAspectRatios.Value))
            .DisposeWith(_disposables);
        config.PreviewGuideAspectRatios.GetObservable()
            .Where(text => SelectedPreset.Value?.Matches(text) != true)
            .Subscribe(text => SelectedPreset.Value = PreviewGuideAspectRatioPreset.Find(text))
            .DisposeWith(_disposables);
        SelectedPreset
            .Where(preset => preset is not null && !preset.Matches(config.PreviewGuideAspectRatios.Value))
            .Subscribe(preset => config.PreviewGuideAspectRatios.Value = preset!.AspectRatios)
            .DisposeWith(_disposables);
    }

    public ReactiveProperty<bool> IsEnabled { get; }

    public IReadOnlyList<PreviewGuideKindItem> Kinds { get; }

    public IReadOnlyReactiveProperty<bool> IsGridEnabled { get; }

    public IReadOnlyReactiveProperty<bool> IsAspectRatioEnabled { get; }

    public ReactiveProperty<Color> Color { get; }

    public ReactiveProperty<double> Opacity { get; }

    public ReactiveProperty<int> GridColumns { get; }

    public ReactiveProperty<int> GridRows { get; }

    public ReactiveProperty<string> AspectRatios { get; }

    public IReadOnlyList<PreviewGuideAspectRatioPreset> Presets => PreviewGuideAspectRatioPreset.All;

    public ReactiveProperty<PreviewGuideAspectRatioPreset?> SelectedPreset { get; }

    // EditorConfig.PreviewGuideGridMaxCells as the grid editors' bound, which is a decimal.
    public static decimal GridMaxCells => EditorConfig.PreviewGuideGridMaxCells;

    // The overlay reads the setting the same way, so the editor shows the color the guides are drawn in.
    internal static Color ParseColor(string? text)
        => Avalonia.Media.Color.TryParse(text, out Color color) ? color : Colors.White;

    public void Dispose()
    {
        _disposables.Dispose();
    }
}

// One guide's check box, which sets or clears its flag in PreviewGuideKinds.
public sealed class PreviewGuideKindItem : IDisposable
{
    private readonly IDisposable _subscription;

    internal PreviewGuideKindItem(EditorConfig config, PreviewGuideKinds kind, string displayName)
    {
        Kind = kind;
        DisplayName = displayName;
        IsChecked = config.PreviewGuideKinds.GetObservable()
            .Select(kinds => kinds.HasFlag(kind))
            .ToReactiveProperty();
        _subscription = IsChecked.Skip(1).Subscribe(on =>
        {
            PreviewGuideKinds kinds = config.PreviewGuideKinds.Value;
            config.PreviewGuideKinds.Value = on ? kinds | kind : kinds & ~kind;
        });
    }

    public PreviewGuideKinds Kind { get; }

    public string DisplayName { get; }

    public ReactiveProperty<bool> IsChecked { get; }

    public void Dispose()
    {
        _subscription.Dispose();
        IsChecked.Dispose();
    }
}

// The frames a platform shows, to check one cut against each of them.
public sealed class PreviewGuideAspectRatioPreset
{
    private readonly PreviewGuideAspectRatio[] _ratios;

    private PreviewGuideAspectRatioPreset(string name, string aspectRatios)
    {
        Name = name;
        AspectRatios = aspectRatios;
        PreviewGuideAspectRatio.TryParseList(aspectRatios, out _ratios);
    }

    // Platform names, which no language translates.
    public static IReadOnlyList<PreviewGuideAspectRatioPreset> All { get; } =
    [
        new("YouTube", "16:9"),
        new("YouTube Shorts", "9:16"),
        new("TikTok", "9:16"),
        new("Instagram Feed", "4:5, 1:1"),
        new("Instagram Reels", "9:16"),
        new("X", "16:9, 1:1"),
    ];

    public string Name { get; }

    public string AspectRatios { get; }

    public bool Matches(string? aspectRatios)
        => PreviewGuideAspectRatio.TryParseList(aspectRatios, out PreviewGuideAspectRatio[] ratios)
           && ratios.SequenceEqual(_ratios);

    // The first preset with these ratios, in this order.
    public static PreviewGuideAspectRatioPreset? Find(string? aspectRatios)
        => All.FirstOrDefault(preset => preset.Matches(aspectRatios));

    public override string ToString() => $"{Name} ({AspectRatios})";
}
