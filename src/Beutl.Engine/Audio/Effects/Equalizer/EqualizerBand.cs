using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;

namespace Beutl.Audio.Effects.Equalizer;

/// <summary>
/// Represents a single band configuration for an equalizer.
/// </summary>
[Display(Name = nameof(AudioStrings.Equalizer_Band), ResourceType = typeof(AudioStrings))]
public sealed partial class EqualizerBand : EngineObject
{
    public EqualizerBand()
    {
        ScanProperties<EqualizerBand>();
    }

    [Display(Name = nameof(AudioStrings.Equalizer_FilterType), ResourceType = typeof(AudioStrings))]
    public IExpressionProperty<BiQuadFilterType> FilterType { get; } = Property.CreateWithExpression(BiQuadFilterType.Peak);

    /// <summary>
    /// Center frequency in Hz.
    /// </summary>
    [Range(20, 20000)]
    [Display(Name = nameof(AudioStrings.Equalizer_Frequency), ResourceType = typeof(AudioStrings))]
    public IExpressionProperty<float> Frequency { get; } = Property.CreateAnimatable(1000f);

    /// <summary>
    /// Gain in dB.
    /// </summary>
    [Range(-24, 24)]
    [Display(Name = nameof(AudioStrings.Equalizer_Gain), ResourceType = typeof(AudioStrings))]
    public IExpressionProperty<float> Gain { get; } = Property.CreateAnimatable(0f);

    /// <summary>
    /// Q factor (determines bandwidth).
    /// </summary>
    [Range(0.1, 18)]
    [Display(Name = nameof(AudioStrings.Equalizer_Q), ResourceType = typeof(AudioStrings))]
    public IExpressionProperty<float> Q { get; } = Property.CreateAnimatable(1f);
}
