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

        SelectedScopeType.Skip(1)
            .Subscribe(_ => RefreshRequested?.Invoke(this, EventArgs.Empty))
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

        // Histogram settings
        json["histogramMode"] = (int)HistogramMode.Value;
    }
}
