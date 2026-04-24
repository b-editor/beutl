using Beutl.Audio.Effects.Equalizer;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.PropertyAdapters;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.Components.EqualizerProperties.ViewModels;

public sealed class EqualizerBandItemViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = [];

    public EqualizerBandItemViewModel(EqualizerBand band, int index, IPropertyEditorFactory factory)
    {
        Band = band;
        Index = index;

        FrequencyAdapter = new AnimatablePropertyAdapter<float>((AnimatableProperty<float>)band.Frequency, band);
        GainAdapter = new AnimatablePropertyAdapter<float>((AnimatableProperty<float>)band.Gain, band);
        QAdapter = new AnimatablePropertyAdapter<float>((AnimatableProperty<float>)band.Q, band);
        FilterTypeAdapter = new EnginePropertyAdapter<BiQuadFilterType>(band.FilterType, band);

        FrequencyEditor = factory.CreateEditor(FrequencyAdapter);
        GainEditor = factory.CreateEditor(GainAdapter);
        QEditor = factory.CreateEditor(QAdapter);
        FilterTypeEditor = factory.CreateEditor(FilterTypeAdapter);

        Label = band.Frequency.SubscribeCurrentValueChange()
            .Select(FormatFrequencyLabel)
            .ToReadOnlyReactivePropertySlim(FormatFrequencyLabel(band.Frequency.CurrentValue))!
            .AddTo(_disposables);

        // BiQuadFilter ignores gainDb for LowPass/HighPass/BandPass/Notch, so showing the gain
        // editor for those types would just produce misleading no-op history entries.
        IsGainVisible = band.FilterType.SubscribeCurrentValueChange()
            .Select(IsGainUsed)
            .ToReadOnlyReactivePropertySlim(IsGainUsed(band.FilterType.CurrentValue))!
            .AddTo(_disposables);
    }

    public EqualizerBand Band { get; }

    public int Index { get; }

    public ReadOnlyReactivePropertySlim<string> Label { get; }

    public ReadOnlyReactivePropertySlim<bool> IsGainVisible { get; }

    public AnimatablePropertyAdapter<float> FrequencyAdapter { get; }

    public AnimatablePropertyAdapter<float> GainAdapter { get; }

    public AnimatablePropertyAdapter<float> QAdapter { get; }

    public EnginePropertyAdapter<BiQuadFilterType> FilterTypeAdapter { get; }

    public IPropertyEditorContext? FrequencyEditor { get; }

    public IPropertyEditorContext? GainEditor { get; }

    public IPropertyEditorContext? QEditor { get; }

    public IPropertyEditorContext? FilterTypeEditor { get; }

    public void Dispose()
    {
        FrequencyEditor?.Dispose();
        GainEditor?.Dispose();
        QEditor?.Dispose();
        FilterTypeEditor?.Dispose();
        _disposables.Dispose();
    }

    private static bool IsGainUsed(BiQuadFilterType type) =>
        type is BiQuadFilterType.Peak or BiQuadFilterType.LowShelf or BiQuadFilterType.HighShelf;

    private static string FormatFrequencyLabel(float freq)
    {
        if (freq >= 1000f)
        {
            double k = freq / 1000.0;
            return k == Math.Floor(k) ? $"{k:0}k" : $"{k:0.#}k";
        }
        return $"{freq:0}";
    }
}
