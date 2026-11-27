using System.ComponentModel.DataAnnotations;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Engine;
using Beutl.Language;
using Beutl.ProjectSystem;

namespace Beutl.Audio.Effects;

[Display(Name = nameof(AudioStrings.DuckingEffect), ResourceType = typeof(AudioStrings))]
public sealed partial class DuckingEffect : AudioEffect
{
    public DuckingEffect()
    {
        ScanProperties<DuckingEffect>();
    }

    [Display(Name = nameof(AudioStrings.DuckingEffect_SourceLayers), Description = nameof(AudioStrings.DuckingEffect_SourceLayers_Description), ResourceType = typeof(AudioStrings))]
    [SuppressResourceClassGeneration]
    public IListProperty<Reference<TimelineLayer>> SourceLayers { get; } = Property.CreateList<Reference<TimelineLayer>>();

    [Range(DuckingNode.MinThresholdDb, 0)]
    [Display(Name = nameof(AudioStrings.DuckingEffect_Threshold), Description = nameof(AudioStrings.DuckingEffect_Threshold_Description), ResourceType = typeof(AudioStrings))]
    [SuppressResourceClassGeneration]
    [NumberStep(10, 1)]
    public IExpressionProperty<float> Threshold { get; } = Property.CreateAnimatable(DuckingNode.DefaultThresholdDb);

    [Range(0, DuckingNode.MaxReductionDb)]
    [Display(Name = nameof(AudioStrings.DuckingEffect_Reduction), Description = nameof(AudioStrings.DuckingEffect_Reduction_Description), ResourceType = typeof(AudioStrings))]
    [SuppressResourceClassGeneration]
    [NumberStep(10, 1)]
    public IExpressionProperty<float> Reduction { get; } = Property.CreateAnimatable(DuckingNode.DefaultReductionDb);

    [Range(0, DuckingNode.MaxAttackMs)]
    [Display(Name = nameof(AudioStrings.DuckingEffect_Attack), Description = nameof(AudioStrings.DuckingEffect_Attack_Description), ResourceType = typeof(AudioStrings))]
    [SuppressResourceClassGeneration]
    [NumberStep(10, 1)]
    public IExpressionProperty<float> Attack { get; } = Property.CreateAnimatable(DuckingNode.DefaultAttackMs);

    [Range(0, DuckingNode.MaxReleaseMs)]
    [Display(Name = nameof(AudioStrings.DuckingEffect_Release), Description = nameof(AudioStrings.DuckingEffect_Release_Description), ResourceType = typeof(AudioStrings))]
    [SuppressResourceClassGeneration]
    [NumberStep(100, 10)]
    public IExpressionProperty<float> Release { get; } = Property.CreateAnimatable(DuckingNode.DefaultReleaseMs);

    public override AudioNode CreateNode(AudioContext context, AudioNode inputNode)
    {
        Guid[] sources = [.. SourceLayers.Select(reference => reference.Id).Where(id => id != Guid.Empty).Distinct()];
        if (sources.Length == 0)
            return inputNode;

        // Read the sidechain on the composition's sample grid. Converting the input here keeps the
        // converter's read-ahead on the input side of the envelope, within the available sidechain block.
        if (context.ChainSampleRate != context.SampleRate)
        {
            var resample = context.CreateResampleNode(context.ChainSampleRate);
            context.Connect(inputNode, resample);
            inputNode = resample;
            context.ChainSampleRate = context.SampleRate;
        }

        var node = context.AddNode(new DuckingNode
        {
            SourceLayerIds = sources,
            ClipStart = this.FindHierarchicalParent<Sound>()?.Start ?? Start,
            Threshold = Threshold,
            Reduction = Reduction,
            Attack = Attack,
            Release = Release,
        });
        context.Connect(inputNode, node);
        return node;
    }
}
