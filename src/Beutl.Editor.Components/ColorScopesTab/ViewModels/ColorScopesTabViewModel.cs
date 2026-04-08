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
    }
}
