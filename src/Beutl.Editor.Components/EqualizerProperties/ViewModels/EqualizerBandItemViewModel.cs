using Beutl.Audio.Effects.Equalizer;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.PropertyAdapters;

namespace Beutl.Editor.Components.EqualizerProperties.ViewModels;

public sealed class EqualizerBandItemViewModel : IDisposable
{
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

        Label = FormatFrequencyLabel(band.Frequency.CurrentValue);
    }

    public EqualizerBand Band { get; }

    public int Index { get; }

    public string Label { get; }

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
    }

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
