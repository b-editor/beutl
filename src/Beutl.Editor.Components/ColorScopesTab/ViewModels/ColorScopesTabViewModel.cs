using System.Text.Json.Nodes;
using Beutl.Editor.Services;
using Beutl.Media;
using Beutl.Media.Source;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;

namespace Beutl.Editor.Components.ColorScopesTab.ViewModels;

public sealed class ColorScopesTabViewModel : IToolContext
{
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
    }

    public event EventHandler? RefreshRequested;

    public string Header => Strings.ColorScopes;

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

    public object? GetService(Type serviceType)
    {
        return _editorContext.GetService(serviceType);
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

        if (json.TryGetPropertyValue("waveformHdrRange", out var waveformHdrNode) && waveformHdrNode is JsonValue waveformHdrValue)
        {
            if (waveformHdrValue.TryGetValue(out float waveformHdr) && waveformHdr >= 0.01f)
            {
                WaveformHdrRange.Value = waveformHdr;
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

        if (json.TryGetPropertyValue("histogramHdrRange", out var histogramHdrNode) && histogramHdrNode is JsonValue histogramHdrValue)
        {
            if (histogramHdrValue.TryGetValue(out float histogramHdr) && histogramHdr >= 0.01f)
            {
                HistogramHdrRange.Value = histogramHdr;
            }
        }

        // False Color settings
        if (json.TryGetPropertyValue("falseColorHdrRange", out var falseColorHdrNode) && falseColorHdrNode is JsonValue falseColorHdrValue)
        {
            if (falseColorHdrValue.TryGetValue(out float falseColorHdr) && falseColorHdr >= 0.01f)
            {
                FalseColorHdrRange.Value = falseColorHdr;
            }
        }

        // Zebra settings
        if (json.TryGetPropertyValue("zebraHighThreshold", out var zebraHighNode) && zebraHighNode is JsonValue zebraHighValue)
        {
            if (zebraHighValue.TryGetValue(out float zebraHigh))
            {
                ZebraHighThreshold.Value = Math.Clamp(zebraHigh, 0f, 1f);
            }
        }

        if (json.TryGetPropertyValue("zebraLowThreshold", out var zebraLowNode) && zebraLowNode is JsonValue zebraLowValue)
        {
            if (zebraLowValue.TryGetValue(out float zebraLow))
            {
                ZebraLowThreshold.Value = Math.Clamp(zebraLow, 0f, 1f);
            }
        }

        if (json.TryGetPropertyValue("zebraHdrRange", out var zebraHdrNode) && zebraHdrNode is JsonValue zebraHdrValue)
        {
            if (zebraHdrValue.TryGetValue(out float zebraHdr) && zebraHdr >= 0.01f)
            {
                ZebraHdrRange.Value = zebraHdr;
            }
        }

        // Shared settings
        if (json.TryGetPropertyValue("colorSpace", out var colorSpaceNode) && colorSpaceNode is JsonValue colorSpaceValue)
        {
            if (colorSpaceValue.TryGetValue(out int colorSpace) && Enum.IsDefined(typeof(ScopeColorSpace), colorSpace))
            {
                ColorSpace.Value = (ScopeColorSpace)colorSpace;
            }
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
