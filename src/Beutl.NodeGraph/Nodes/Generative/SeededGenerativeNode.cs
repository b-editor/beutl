using System.ComponentModel.DataAnnotations;
using Beutl.Language;
using Beutl.NodeGraph.Composition;
using Beutl.NodeGraph.Generative;

namespace Beutl.NodeGraph.Nodes.Generative;

/// <summary>What happens to the seed after each generation, as in ComfyUI.</summary>
public enum GenerativeSeedControl
{
    /// <summary>Keep the seed; queueing again reuses the result.</summary>
    [Display(Name = nameof(NodeGraphStrings.SeedControl_Fixed), ResourceType = typeof(NodeGraphStrings))]
    Fixed,
    /// <summary>Add one after each generation, so the next queue makes a new variation.</summary>
    [Display(Name = nameof(NodeGraphStrings.SeedControl_Increment), ResourceType = typeof(NodeGraphStrings))]
    Increment,
    /// <summary>Pick a new random seed after each generation.</summary>
    [Display(Name = nameof(NodeGraphStrings.SeedControl_Randomize), ResourceType = typeof(NodeGraphStrings))]
    Randomize,
    /// <summary>Send no seed and let the model choose.</summary>
    [Display(Name = nameof(NodeGraphStrings.SeedControl_ModelDefault), ResourceType = typeof(NodeGraphStrings))]
    ModelDefault,
}

/// <summary>A generation node with a seed, which its seed control moves on after each generation.</summary>
public abstract partial class SeededGenerativeNode : GenerativeNode
{
    // Matches the range the API accepts (AiRequestLimits in Beutl.Api).
    internal const int MinSeed = 0;
    internal const int MaxSeed = int.MaxValue;

    // Set by each node's constructor, which adds them where they belong among its ports.
    public InputPort<int> Seed { get; private protected init; } = null!;

    public InputPort<GenerativeSeedControl> SeedControl { get; private protected init; } = null!;

    /// <summary>Moves the seed on after a generation, as the seed control says.</summary>
    protected internal override void OnGenerated(GenerationRecord record)
    {
        GenerativeSeedControl control = SeedControl.Property?.GetValue() ?? GenerativeSeedControl.Fixed;
        if (Seed.Property is not { } seed)
            return;
        switch (control)
        {
            case GenerativeSeedControl.Increment:
                seed.SetValue(seed.GetValue() == MaxSeed ? MinSeed : seed.GetValue() + 1);
                break;
            case GenerativeSeedControl.Randomize:
                seed.SetValue(Random.Shared.Next(MinSeed, MaxSeed));
                break;
        }
    }

    protected internal override GenerativeRequest BuildVariation(
        GraphNode.Resource resource,
        GraphCompositionContext context,
        int index)
    {
        GenerativeRequest request = BuildRequest(resource, context);
        if (index == 0 || request is not ISeededGenerativeRequest { Seed: int seed } seeded)
            return request;

        int varied = VarySeed(seed, index);
        return seeded.WithSeed(varied, ((Resource)resource).ComputeParameterFingerprint(varied));
    }

    protected internal override void ApplyRequestInputs(GenerativeRequest request)
    {
        ApplySeedInput((request as ISeededGenerativeRequest)?.Seed);
    }

    protected internal override void ApplyRecordInputs(GenerationRecord record)
    {
        ApplySeedInput(record.Seed);
    }

    /// <summary>The seed of the <paramref name="index"/>th variation, wrapped into the seed range.</summary>
    private static int VarySeed(int seed, int index)
        => (int)(((long)seed + index) % ((long)MaxSeed + 1));

    /// <summary>Puts a reproduced seed on the seed input unless a connection drives it.</summary>
    private void ApplySeedInput(int? seed)
    {
        if (seed is int value && Seed.Connection.IsNull)
            Seed.Property?.SetValue(value);
    }

    public partial class Resource
    {
        /// <summary>The seed a request sends; none when the model chooses.</summary>
        internal int? RequestSeed => SeedControl == GenerativeSeedControl.ModelDefault ? null : Seed;

        internal string ComputeParameterFingerprint() => ComputeParameterFingerprint(null);

        internal abstract string ComputeParameterFingerprint(int? seedOverride);
    }
}
