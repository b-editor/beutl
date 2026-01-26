using System.ComponentModel.DataAnnotations;
using Beutl.Audio.Effects.Equalizer;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Engine;
using Beutl.Language;

namespace Beutl.Audio.Effects;

/// <summary>
/// Parametric equalizer effect.
/// Allows users to freely add, remove, and configure all parameters for each band.
/// </summary>
[Display(Name = nameof(Strings.ParametricEqualizer), ResourceType = typeof(Strings))]
public sealed partial class ParametricEqualizerEffect : AudioEffect
{
    public ParametricEqualizerEffect()
    {
        ScanProperties<ParametricEqualizerEffect>();
    }

    /// <summary>
    /// List of equalizer bands.
    /// </summary>
    [Display(Name = nameof(Strings.Bands), ResourceType = typeof(Strings))]
    public IListProperty<EqualizerBand> Bands { get; } = Property.CreateList<EqualizerBand>();

    public override AudioNode CreateNode(AudioContext context, AudioNode inputNode)
    {
        var enabledBands = Bands.Where(band => band.IsEnabled).ToList();

        if (enabledBands.Count == 0)
        {
            return inputNode;
        }

        var equalizerNode = context.AddNode(new EqualizerNode
        {
            Bands = enabledBands
        });

        context.Connect(inputNode, equalizerNode);
        return equalizerNode;
    }
}
