using System.ComponentModel.DataAnnotations;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Engine;
using Beutl.Language;

using static Beutl.Audio.Effects.NoiseReductionParameters;

namespace Beutl.Audio.Effects;

/// <summary>
/// Reduces steady background noise such as hiss, hum, fans, and room tone while keeping speech.
/// </summary>
/// <remarks>
/// The noise floor is learned continuously from the audio itself, so no noise sample is needed. Sounds
/// that sustain longer than <see cref="Adaptation"/> are indistinguishable from steady noise and are
/// reduced as well. The effect adds no latency: it reads its input ahead of the output.
/// </remarks>
[Display(Name = nameof(AudioStrings.NoiseReductionEffect), ResourceType = typeof(AudioStrings))]
public sealed partial class NoiseReductionEffect : AudioEffect
{
    public NoiseReductionEffect()
    {
        ScanProperties<NoiseReductionEffect>();
    }

    /// <summary>The deepest attenuation applied to noise, in decibels. 0 dB disables the effect.</summary>
    [Range(MinReductionDb, MaxReductionDb)]
    [Display(Name = nameof(AudioStrings.NoiseReductionEffect_Reduction), Description = nameof(AudioStrings.NoiseReductionEffect_Reduction_Description), ResourceType = typeof(AudioStrings))]
    [SuppressResourceClassGeneration]
    [NumberStep(1, 0.1)]
    public IProperty<float> Reduction { get; } = Property.CreateAnimatable(DefaultReductionDb);

    /// <summary>How far the estimated noise floor is raised before audio is compared with it, in decibels.</summary>
    [Range(MinSensitivityDb, MaxSensitivityDb)]
    [Display(Name = nameof(AudioStrings.NoiseReductionEffect_Sensitivity), Description = nameof(AudioStrings.NoiseReductionEffect_Sensitivity_Description), ResourceType = typeof(AudioStrings))]
    [SuppressResourceClassGeneration]
    [NumberStep(1, 0.1)]
    public IProperty<float> Sensitivity { get; } = Property.CreateAnimatable(DefaultSensitivityDb);

    /// <summary>How smoothly the reduction follows the audio over time, in percent.</summary>
    [Range(MinSmoothing, MaxSmoothing)]
    [Display(Name = nameof(AudioStrings.NoiseReductionEffect_Smoothing), Description = nameof(AudioStrings.NoiseReductionEffect_Smoothing_Description), ResourceType = typeof(AudioStrings))]
    [SuppressResourceClassGeneration]
    [NumberStep(10, 1)]
    public IProperty<float> Smoothing { get; } = Property.CreateAnimatable(DefaultSmoothing);

    /// <summary>How much past audio the noise floor is learned from, in seconds.</summary>
    [Range(MinAdaptationSeconds, MaxAdaptationSeconds)]
    [Display(Name = nameof(AudioStrings.NoiseReductionEffect_Adaptation), Description = nameof(AudioStrings.NoiseReductionEffect_Adaptation_Description), ResourceType = typeof(AudioStrings))]
    [SuppressResourceClassGeneration]
    [NumberStep(1, 0.1)]
    public IProperty<float> Adaptation { get; } = Property.CreateAnimatable(DefaultAdaptationSeconds);

    public override AudioNode CreateNode(AudioContext context, AudioNode inputNode)
    {
        // Reuse the node across graph updates to keep its position in the stream. Reused upstream
        // nodes may produce different audio after the update, so the node refetches what it has read
        // ahead and relearns the noise floor from it.
        var node = context.CreateNode(
            this,
            static effect => new NoiseReductionNode
            {
                Reduction = effect.Reduction,
                Sensitivity = effect.Sensitivity,
                Smoothing = effect.Smoothing,
                Adaptation = effect.Adaptation
            },
            static (_, existing) => existing.InvalidateInput(),
            static (effect, existing) => ReferenceEquals(existing.Reduction, effect.Reduction));
        context.Connect(inputNode, node);
        return node;
    }
}
