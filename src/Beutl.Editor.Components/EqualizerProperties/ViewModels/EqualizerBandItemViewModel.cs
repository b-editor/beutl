using Beutl.Animation;
using Beutl.Audio.Effects.Equalizer;
using Beutl.Composition;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.ProjectSystem;
using Beutl.PropertyAdapters;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.Components.EqualizerProperties.ViewModels;

public sealed class EqualizerBandItemViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = [];
    private readonly Element? _element;
    private readonly IServiceProvider? _services;

    public EqualizerBandItemViewModel(EqualizerBand band, int index, IPropertyEditorFactory factory,
        Element? element, IServiceProvider? services)
    {
        Band = band;
        Index = index;
        _element = element;
        _services = services;

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

    public void Commit()
    {
        _services?.GetService<HistoryManager>()?.Commit();
    }

    public float GetEffectiveValue(IProperty<float> property, TimeSpan globalTime)
    {
        if (GetEditingKeyFrame(property, globalTime) is { } kf)
            return kf.Value;
        if (property.HasExpression)
            return property.GetValue(new CompositionContext(globalTime));
        return property.CurrentValue;
    }

    public void SetPropertyValue(IProperty<float> property, TimeSpan globalTime, float value)
    {
        if (GetEditingKeyFrame(property, globalTime) is { } kf)
            kf.Value = value;
        else
            property.CurrentValue = value;
    }

    public bool CanEditProperty(IProperty<float> property, TimeSpan globalTime)
    {
        if (property.HasExpression) return false;
        if (property.Animation is null) return true;
        return GetEditingKeyFrame(property, globalTime) is not null;
    }

    public KeyFrame<float>? GetEditingKeyFrame(IProperty<float> property, TimeSpan globalTime)
    {
        var adapter = ReferenceEquals(property, Band.Frequency) ? FrequencyAdapter
            : ReferenceEquals(property, Band.Gain) ? GainAdapter
            : ReferenceEquals(property, Band.Q) ? QAdapter
            : null;
        if (adapter == null) return null;
        return FindEditingKeyFrame(adapter, globalTime);
    }

    private KeyFrame<float>? FindEditingKeyFrame(AnimatablePropertyAdapter<float> adapter, TimeSpan globalTime)
    {
        if (adapter.Expression != null) return null;
        if (adapter.Animation is not IKeyFrameAnimation { KeyFrames: { Count: > 0 } keyFrames })
            return null;

        TimeSpan time = globalTime;
        if (adapter.Animation is { UseGlobalClock: false } && _element != null)
            time -= _element.Start;

        int idx = Math.Clamp(keyFrames.IndexAtOrCount(time), 0, keyFrames.Count - 1);
        return keyFrames[idx] as KeyFrame<float>;
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
