using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;

namespace Beutl.Audio.Effects.Equalizer;

/// <summary>
/// Represents a single band configuration for an equalizer.
/// </summary>
[Display(Name = nameof(Strings.EqualizerBand), ResourceType = typeof(Strings))]
public sealed partial class EqualizerBand : EngineObject
{
    public EqualizerBand()
    {
        ScanProperties<EqualizerBand>();
    }

    /// <summary>
    /// Filter type.
    /// </summary>
    [Display(Name = nameof(Strings.FilterType), ResourceType = typeof(Strings))]
    public IProperty<BiQuadFilterType> FilterType { get; } = Property.Create(BiQuadFilterType.Peak);

    /// <summary>
    /// Center frequency in Hz.
    /// </summary>
    [Range(20, 20000)]
    [Display(Name = nameof(Strings.Frequency), ResourceType = typeof(Strings))]
    public IProperty<float> Frequency { get; } = Property.CreateAnimatable(1000f);

    /// <summary>
    /// Gain in dB.
    /// </summary>
    [Range(-24, 24)]
    [Display(Name = nameof(Strings.Gain), ResourceType = typeof(Strings))]
    public IProperty<float> Gain { get; } = Property.CreateAnimatable(0f);

    /// <summary>
    /// Q factor (determines bandwidth).
    /// </summary>
    [Range(0.1, 18)]
    [Display(Name = nameof(Strings.Q), ResourceType = typeof(Strings))]
    public IProperty<float> Q { get; } = Property.CreateAnimatable(1f);
}
