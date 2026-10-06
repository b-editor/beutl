using System.Text.Json.Nodes;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Services;
using Beutl.Media;
using Beutl.Media.Source;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;

namespace Beutl.Editor.Components.ColorScopesTab.ViewModels;

public sealed class ColorScopesTabViewModel : IToolContext
{
    // Restored HDR ranges below this are ignored; the scope controls clamp to the same floor.
    private const float MinHdrRange = 0.01f;

    private readonly CompositeDisposable _disposables = [];
    private readonly IEditorContext _editorContext;
    private readonly IPreviewPlayer _player;

    public ColorScopesTabViewModel(IEditorContext editorContext)
    {
        _editorContext = editorContext;
        _player = editorContext.GetRequiredService<IPreviewPlayer>();
        SourceBitmap.Value = _player.PreviewImage.Value;

        // Update scope after rendering is complete
        _player.AfterRendered.CombineLatest(IsSelected)
            .Subscribe(_ =>
            {
                if (!IsSelected.Value) return;

                SourceBitmap.Value = _player.PreviewImage.Value;
                RefreshRequested?.Invoke(this, EventArgs.Empty);
            })
            .DisposeWith(_disposables);

        SelectedScopeType.Skip(1)
            .Subscribe(_ => RefreshRequested?.Invoke(this, EventArgs.Empty))
            .DisposeWith(_disposables);

        Header = SelectedScopeType
            .Select(t => ToolTabHeaderHelper.Compose(Strings.ColorScopes, LocalizeScopeType(t)))
            .ToReadOnlyReactivePropertySlim(
                ToolTabHeaderHelper.Compose(Strings.ColorScopes, LocalizeScopeType(SelectedScopeType.Value)))
            .DisposeWith(_disposables)!;
    }

    public event EventHandler? RefreshRequested;

    public IReadOnlyReactiveProperty<string> Header { get; }

    public ToolTabExtension Extension => ColorScopesTabExtension.Instance;

    public ReactivePropertySlim<ColorScopeType> SelectedScopeType { get; } = new(ColorScopeType.Waveform);

    public ReactivePropertySlim<Ref<Bitmap>?> SourceBitmap { get; } = new();

    // Waveform settings
    public ReactivePropertySlim<WaveformMode> WaveformMode { get; } = new(ViewModels.WaveformMode.RgbOverlay);

    public ReactivePropertySlim<float> WaveformHdrRange { get; } = new(1.0f);

    // Histogram settings
    public ReactivePropertySlim<HistogramMode> HistogramMode { get; } = new(ViewModels.HistogramMode.Parade);

    public ReactivePropertySlim<float> HistogramHdrRange { get; } = new(1.0f);

    // False Color settings
    public ReactivePropertySlim<float> FalseColorHdrRange { get; } = new(1.0f);

    // Zebra settings
    public ReactivePropertySlim<float> ZebraHighThreshold { get; } = new(0.95f);

    public ReactivePropertySlim<float> ZebraLowThreshold { get; } = new(0.03f);

    public ReactivePropertySlim<float> ZebraHdrRange { get; } = new(1.0f);

    // Shared settings
    public ReactivePropertySlim<ScopeColorSpace> ColorSpace { get; } = new(ScopeColorSpace.Gamma);

    public IReactiveProperty<bool> IsSelected { get; } = new ReactiveProperty<bool>();

    public void Dispose()
    {
        _disposables.Dispose();
    }

    internal static string LocalizeScopeType(ColorScopeType type) => type switch
    {
        ColorScopeType.Waveform => Strings.Waveform,
        ColorScopeType.Histogram => Strings.Histogram,
        ColorScopeType.Vectorscope => Strings.Vectorscope,
        ColorScopeType.FalseColor => Strings.FalseColor,
        ColorScopeType.Zebra => Strings.Zebra,
        _ => Strings.ColorScopes,
    };

    public object? GetService(Type serviceType)
    {
        return _editorContext.GetService(serviceType);
    }

    public void ReadFromJson(JsonObject json)
    {
        if (json.TryGetPropertyValueAsJsonValue("scopeType", out int scopeType)
            && Enum.IsDefined(typeof(ColorScopeType), scopeType))
        {
            SelectedScopeType.Value = (ColorScopeType)scopeType;
        }

        // Waveform settings
        if (json.TryGetPropertyValueAsJsonValue("waveformMode", out int mode)
            && Enum.IsDefined(typeof(WaveformMode), mode))
        {
            WaveformMode.Value = (WaveformMode)mode;
        }

        if (json.TryGetPropertyValueAsJsonValue("waveformHdrRange", out float waveformHdr) && waveformHdr >= MinHdrRange)
        {
            WaveformHdrRange.Value = waveformHdr;
        }

        // Histogram settings
        if (json.TryGetPropertyValueAsJsonValue("histogramMode", out int histogramMode)
            && Enum.IsDefined(typeof(HistogramMode), histogramMode))
        {
            HistogramMode.Value = (HistogramMode)histogramMode;
        }

        if (json.TryGetPropertyValueAsJsonValue("histogramHdrRange", out float histogramHdr) && histogramHdr >= MinHdrRange)
        {
            HistogramHdrRange.Value = histogramHdr;
        }

        // False Color settings
        if (json.TryGetPropertyValueAsJsonValue("falseColorHdrRange", out float falseColorHdr) && falseColorHdr >= MinHdrRange)
        {
            FalseColorHdrRange.Value = falseColorHdr;
        }

        // Zebra settings
        if (json.TryGetPropertyValueAsJsonValue("zebraHighThreshold", out float zebraHigh))
        {
            ZebraHighThreshold.Value = Math.Clamp(zebraHigh, 0f, 1f);
        }

        if (json.TryGetPropertyValueAsJsonValue("zebraLowThreshold", out float zebraLow))
        {
            ZebraLowThreshold.Value = Math.Clamp(zebraLow, 0f, 1f);
        }

        if (json.TryGetPropertyValueAsJsonValue("zebraHdrRange", out float zebraHdr) && zebraHdr >= MinHdrRange)
        {
            ZebraHdrRange.Value = zebraHdr;
        }

        // Shared settings
        if (json.TryGetPropertyValueAsJsonValue("colorSpace", out int colorSpace)
            && Enum.IsDefined(typeof(ScopeColorSpace), colorSpace))
        {
            ColorSpace.Value = (ScopeColorSpace)colorSpace;
        }
    }

    public void WriteToJson(JsonObject json)
    {
        json["scopeType"] = (int)SelectedScopeType.Value;

        // Waveform settings
        json["waveformMode"] = (int)WaveformMode.Value;
        json["waveformHdrRange"] = WaveformHdrRange.Value;

        // Histogram settings
        json["histogramMode"] = (int)HistogramMode.Value;
        json["histogramHdrRange"] = HistogramHdrRange.Value;

        // False Color settings
        json["falseColorHdrRange"] = FalseColorHdrRange.Value;

        // Zebra settings
        json["zebraHighThreshold"] = ZebraHighThreshold.Value;
        json["zebraLowThreshold"] = ZebraLowThreshold.Value;
        json["zebraHdrRange"] = ZebraHdrRange.Value;

        // Shared settings
        json["colorSpace"] = (int)ColorSpace.Value;
    }
}
