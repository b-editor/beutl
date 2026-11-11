using System.ComponentModel.DataAnnotations;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Engine;
using Beutl.Language;

namespace Beutl.Audio.Effects;

/// <summary>
/// Converts audio from its source's sample rate to the project's with the chosen <see cref="Method"/>, in place of the
/// conversion that otherwise follows the effects.
/// </summary>
/// <remarks>
/// The effects before this one run at the source's rate and those after it at the project's. Audio that is at the
/// project's rate already, such as a group's, passes through. The effect adds no latency: it reads its input ahead of
/// the output.
/// </remarks>
[Display(Name = nameof(AudioStrings.ResamplingEffect), ResourceType = typeof(AudioStrings))]
public sealed partial class ResamplingEffect : AudioEffect
{
    public ResamplingEffect()
    {
        ScanProperties<ResamplingEffect>();
    }

    [Display(Name = nameof(AudioStrings.ResamplingEffect_Method), Description = nameof(AudioStrings.ResamplingEffect_Method_Description), ResourceType = typeof(AudioStrings))]
    [SuppressResourceClassGeneration]
    public IExpressionProperty<ResamplingMethod> Method { get; } = Property.CreateExpression(ResamplingMethod.Hold);

    public override AudioNode CreateNode(AudioContext context, AudioNode inputNode)
    {
        int sourceSampleRate = context.ChainSampleRate;
        if (sourceSampleRate == context.SampleRate)
            return inputNode;

        var node = context.AddNode(new ResamplingNode
        {
            SourceSampleRate = sourceSampleRate,
            Method = Method,
            ChannelCount = context.ChannelCount
        });
        context.Connect(inputNode, node);
        context.ChainSampleRate = context.SampleRate;
        return node;
    }
}
