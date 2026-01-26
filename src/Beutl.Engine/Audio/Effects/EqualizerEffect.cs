using System.ComponentModel.DataAnnotations;
using Beutl.Audio.Effects.Equalizer;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Editor;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Serialization;

namespace Beutl.Audio.Effects;

/// <summary>
/// Graphic equalizer effect.
/// Has fixed frequency bands with only gain adjustment available.
/// </summary>
[Display(Name = nameof(Strings.Equalizer), ResourceType = typeof(Strings))]
public sealed partial class EqualizerEffect : AudioEffect
{
    // Center frequencies for each preset (Hz)
    private static readonly float[] Frequencies5Band = { 60f, 230f, 910f, 3600f, 14000f };

    private static readonly float[] Frequencies10Band =
    {
        31f, 62f, 125f, 250f, 500f, 1000f, 2000f, 4000f, 8000f, 16000f
    };

    private static readonly float[] Frequencies15Band =
    {
        25f, 40f, 63f, 100f, 160f, 250f, 400f, 630f, 1000f, 1600f, 2500f, 4000f, 6300f, 10000f, 16000f
    };

    private static readonly float[] Frequencies31Band =
    {
        20f, 25f, 31.5f, 40f, 50f, 63f, 80f, 100f, 125f, 160f, 200f, 250f, 315f, 400f, 500f, 630f, 800f, 1000f,
        1250f, 1600f, 2000f, 2500f, 3150f, 4000f, 5000f, 6300f, 8000f, 10000f, 12500f, 16000f, 20000f
    };

    public EqualizerEffect()
    {
        ScanProperties<EqualizerEffect>();
        InitializeBands(BandCountPreset.Bands10);
        BandCountOption.ValueChanged += OnBandCountOptionOnValueChanged;
    }

    /// <summary>
    /// Band count preset.
    /// </summary>
    [Display(Name = nameof(Strings.BandCount), ResourceType = typeof(Strings))]
    public IProperty<BandCountPreset> BandCountOption { get; } = Property.Create(BandCountPreset.Bands10);

    /// <summary>
    /// List of equalizer bands (fixed frequencies).
    /// </summary>
    [Display(Name = nameof(Strings.Bands), ResourceType = typeof(Strings))]
    public IListProperty<EqualizerBand> Bands { get; } = Property.CreateList<EqualizerBand>();

    private void OnBandCountOptionOnValueChanged(object? o,
        PropertyValueChangedEventArgs<BandCountPreset> propertyValueChangedEventArgs)
    {
        if (RecordingSuppression.IsSuppressed) return;
        InitializeBands(BandCountOption.CurrentValue);
    }

    /// <summary>
    /// Initializes bands based on the specified preset.
    /// </summary>
    private void InitializeBands(BandCountPreset preset)
    {
        var frequencies = preset switch
        {
            BandCountPreset.Bands5 => Frequencies5Band,
            BandCountPreset.Bands10 => Frequencies10Band,
            BandCountPreset.Bands15 => Frequencies15Band,
            BandCountPreset.Bands31 => Frequencies31Band,
            _ => Frequencies10Band
        };

        // Calculate Q value for graphic EQ
        // Q value considering overlap between adjacent bands
        float q = preset switch
        {
            BandCountPreset.Bands5 => 1.4f,
            BandCountPreset.Bands10 => 1.4f,
            BandCountPreset.Bands15 => 2.0f,
            BandCountPreset.Bands31 => 4.3f,
            _ => 1.4f
        };

        Bands.Clear();

        foreach (float frequency in frequencies)
        {
            var band = new EqualizerBand();
            band.FilterType.CurrentValue = BiQuadFilterType.Peak;
            band.Frequency.CurrentValue = frequency;
            band.Gain.CurrentValue = 0f;
            band.Q.CurrentValue = q;
            Bands.Add(band);
        }
    }

    public override AudioNode CreateNode(AudioContext context, AudioNode inputNode)
    {
        var enabledBands = Bands.Where(band => band.IsEnabled).ToList();

        if (enabledBands.Count == 0)
        {
            return inputNode;
        }

        var equalizerNode = context.AddNode(new EqualizerNode { Bands = enabledBands });

        context.Connect(inputNode, equalizerNode);
        return equalizerNode;
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        try
        {
            BandCountOption.ValueChanged -= OnBandCountOptionOnValueChanged;
            base.Deserialize(context);
        }
        finally
        {
            BandCountOption.ValueChanged += OnBandCountOptionOnValueChanged;
        }
    }
}
