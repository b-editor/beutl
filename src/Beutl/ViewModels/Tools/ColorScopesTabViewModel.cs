using System.Text.Json.Nodes;
using Avalonia.Media.Imaging;
using Beutl.Services.PrimitiveImpls;
using Beutl.Views.Tools.Scopes;
using Reactive.Bindings;

namespace Beutl.ViewModels.Tools;

public enum ColorScopeType
{
    Waveform,
    Histogram,
    Vectorscope
}

public sealed class ColorScopesTabViewModel : IToolContext
{
    private readonly CompositeDisposable _disposables = [];
    private readonly EditViewModel _editViewModel;

    public ColorScopesTabViewModel(EditViewModel editViewModel)
    {
        _editViewModel = editViewModel;

        // Update scope after rendering is complete
        editViewModel.Player.AfterRendered
            .Subscribe(_ =>
            {
                SourceBitmap.Value = editViewModel.Player.PreviewImage.Value as WriteableBitmap;
                RefreshRequested?.Invoke(this, EventArgs.Empty);
            })
            .DisposeWith(_disposables);
    }

    public event EventHandler? RefreshRequested;

    public string Header => Strings.ColorScopes;

    public ToolTabExtension Extension => ColorScopesTabExtension.Instance;

    public ReactivePropertySlim<ColorScopeType> SelectedScopeType { get; } = new(ColorScopeType.Waveform);

    public ReactivePropertySlim<WriteableBitmap?> SourceBitmap { get; } = new();

    // Waveform settings
    public ReactivePropertySlim<WaveformMode> WaveformMode { get; } = new(Views.Tools.Scopes.WaveformMode.Luma);

    // Histogram settings
    public ReactivePropertySlim<HistogramMode> HistogramMode { get; } = new(Views.Tools.Scopes.HistogramMode.Overlay);

    public ReactivePropertySlim<float> WaveformThickness { get; } = new(1.25f);

    public ReactivePropertySlim<float> WaveformGain { get; } = new(2.0f);

    public ReactivePropertySlim<bool> WaveformShowGrid { get; } = new(true);

    public IReactiveProperty<bool> IsSelected { get; } = new ReactiveProperty<bool>();

    public IReactiveProperty<ToolTabExtension.TabPlacement> Placement { get; } =
        new ReactivePropertySlim<ToolTabExtension.TabPlacement>(ToolTabExtension.TabPlacement.RightUpperTop);

    public IReactiveProperty<ToolTabExtension.TabDisplayMode> DisplayMode { get; } =
        new ReactivePropertySlim<ToolTabExtension.TabDisplayMode>();

    public void Dispose()
    {
        _disposables.Dispose();
    }

    public object? GetService(Type serviceType)
    {
        return null;
    }

    public void ReadFromJson(JsonObject json)
    {
        if (json.TryGetPropertyValue("scopeType", out var scopeTypeNode) && scopeTypeNode is JsonValue scopeTypeValue)
        {
            if (scopeTypeValue.TryGetValue(out int scopeType) && Enum.IsDefined(typeof(ColorScopeType), scopeType))
            {
                SelectedScopeType.Value = (ColorScopeType)scopeType;
            }
        }

        // Waveform settings
        if (json.TryGetPropertyValue("waveformMode", out var modeNode) && modeNode is JsonValue modeValue)
        {
            if (modeValue.TryGetValue(out int mode) && Enum.IsDefined(typeof(WaveformMode), mode))
            {
                WaveformMode.Value = (WaveformMode)mode;
            }
        }

        if (json.TryGetPropertyValue("waveformThickness", out var thicknessNode) && thicknessNode is JsonValue thicknessValue)
        {
            if (thicknessValue.TryGetValue(out float thickness))
            {
                WaveformThickness.Value = thickness;
            }
        }

        if (json.TryGetPropertyValue("waveformGain", out var gainNode) && gainNode is JsonValue gainValue)
        {
            if (gainValue.TryGetValue(out float gain))
            {
                WaveformGain.Value = gain;
            }
        }

        if (json.TryGetPropertyValue("waveformShowGrid", out var showGridNode) && showGridNode is JsonValue showGridValue)
        {
            if (showGridValue.TryGetValue(out bool showGrid))
            {
                WaveformShowGrid.Value = showGrid;
            }
        }

        // Histogram settings
        if (json.TryGetPropertyValue("histogramMode", out var histogramModeNode) && histogramModeNode is JsonValue histogramModeValue)
        {
            if (histogramModeValue.TryGetValue(out int histogramMode) && Enum.IsDefined(typeof(HistogramMode), histogramMode))
            {
                HistogramMode.Value = (HistogramMode)histogramMode;
            }
        }
    }

    public void WriteToJson(JsonObject json)
    {
        json["scopeType"] = (int)SelectedScopeType.Value;

        // Waveform settings
        json["waveformMode"] = (int)WaveformMode.Value;
        json["waveformThickness"] = WaveformThickness.Value;
        json["waveformGain"] = WaveformGain.Value;
        json["waveformShowGrid"] = WaveformShowGrid.Value;

        // Histogram settings
        json["histogramMode"] = (int)HistogramMode.Value;
    }
}
