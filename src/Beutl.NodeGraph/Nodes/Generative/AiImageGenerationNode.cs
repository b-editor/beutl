using System.ComponentModel.DataAnnotations;
using Beutl.Extensibility;
using Beutl.Graphics.Rendering;
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

/// <summary>Generates a picture from a prompt and optional reference pictures.</summary>
public sealed partial class AiImageGenerationNode : GenerativeNode, IPromptLibraryTarget
{
    // Matches the range the API accepts (AiRequestLimits in Beutl.Api).
    internal const int MinSeed = 0;
    internal const int MaxSeed = int.MaxValue;

    // The AI tab's fallback when the scene gives no better shape.
    internal const string DefaultAspectRatio = "16:9";

    public AiImageGenerationNode()
    {
        Output = AddOutput<ImageSourceRenderNode?>("Image", NodePortDisplays.Image);
        // In the AI tab's order. Members are matched by name when loading, so reordering is safe.
        Prompt = AddInput<string>("Prompt", NodePortDisplays.Prompt);
        AspectRatio = AddInput<string>("AspectRatio", NodePortDisplays.AspectRatio);
        Model = AddInput<string>("Model", NodePortDisplays.Model);
        Background = AddInput<string>("Background", NodePortDisplays.Background);
        Seed = AddInput<int>("Seed", NodePortDisplays.Seed);
        SeedControl = AddInput<GenerativeSeedControl>("SeedControl", NodePortDisplays.SeedControl);
        References = AddListInput<RenderNode?>("References", NodePortDisplays.References);
        AddGenerativeMonitors(NodePortDisplays.Preview, NodePortDisplays.Status);

        Prompt.Property?.SetValue(string.Empty);
        Model.Property?.SetValue(string.Empty);
        AspectRatio.Property?.SetValue(DefaultAspectRatio);
        Background.Property?.SetValue("auto");
        Seed.Property?.SetValue(Random.Shared.Next(MinSeed, MaxSeed));
        UseMultilineEditor(Prompt);
        RegisterChoice(Model, GenerativeChoiceKind.Model);
        RegisterChoice(AspectRatio, GenerativeChoiceKind.AspectRatio);
        RegisterChoice(Background, GenerativeChoiceKind.Background);
    }

    public override IPropertyAdapter<string>? ModelProperty => Model.Property;

    public GenerativeOperation PromptOperation => Operation;

    public bool CanApplyPrompt => Prompt.Connection.IsNull;

    public string ComposePrompt() => Prompt.Property?.GetValue()?.Trim() ?? string.Empty;

    public void ApplyPrompt(string prompt) => Prompt.Property?.SetValue(prompt);

    public override GenerativeOperation Operation => GenerativeOperation.ImageGeneration;

    public override string CatalogOperationId => "image.generate";

    public OutputPort<ImageSourceRenderNode?> Output { get; }

    /// <summary>The prompt text; connect a prompt node for style, composition and exclusions.</summary>
    public InputPort<string> Prompt { get; }

    public ListInputPort<RenderNode?> References { get; }

    /// <summary>A model id from the catalog; empty for the server's default.</summary>
    public InputPort<string> Model { get; }

    public InputPort<string> AspectRatio { get; }

    public InputPort<string> Background { get; }

    public InputPort<int> Seed { get; }

    public InputPort<GenerativeSeedControl> SeedControl { get; }

    protected internal override GenerativeRequest BuildRequest(GraphNode.Resource resource, GraphCompositionContext context)
    {
        var r = (Resource)resource;
        if (string.IsNullOrWhiteSpace(r.Prompt))
            throw new GenerativeExecutionException(NodeGraphStrings.Generative_PromptRequired);

        List<RenderNode?> referenceNodes = context.CollectListInputValues(References);
        var references = new List<GenerativeImageInput>(referenceNodes.Count);
        for (int i = 0; i < referenceNodes.Count; i++)
        {
            if (RasterizeInput(referenceNodes[i], $"reference-{i + 1}", context) is { } input)
                references.Add(input);
        }

        return new AiImageGenerationNodeRequest(this)
        {
            Prompt = r.Prompt!.Trim(),
            AspectRatio = string.IsNullOrWhiteSpace(r.AspectRatio) ? DefaultAspectRatio : r.AspectRatio!.Trim(),
            Background = string.IsNullOrWhiteSpace(r.Background) ? "auto" : r.Background!.Trim(),
            Seed = SeedFor(r),
            ModelId = string.IsNullOrWhiteSpace(r.Model) ? null : r.Model!.Trim(),
            References = references,
            RequestKeySeed = RequestKeySeed,
            ParameterFingerprint = r.ComputeParameterFingerprint(),
        };
    }

    protected internal override void OnGenerated(GenerationRecord record) => AdvanceSeed(Seed, SeedControl);

    /// <summary>Moves the seed on after a generation, as the seed control says.</summary>
    internal static void AdvanceSeed(InputPort<int> seedPort, InputPort<GenerativeSeedControl> controlPort)
    {
        GenerativeSeedControl control = controlPort.Property?.GetValue() ?? GenerativeSeedControl.Fixed;
        if (seedPort.Property is not { } seed)
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

    private static int? SeedFor(Resource r)
        => r.SeedControl == GenerativeSeedControl.ModelDefault ? null : r.Seed;

    protected internal override GenerativeRequest BuildVariation(
        GraphNode.Resource resource,
        GraphCompositionContext context,
        int index)
    {
        var request = (AiImageGenerationNodeRequest)BuildRequest(resource, context);
        if (index == 0 || request.Seed is not int seed)
            return request;

        int varied = (int)(((long)seed + index) % ((long)AiImageGenerationNode.MaxSeed + 1));
        return request with
        {
            Seed = varied,
            ParameterFingerprint = ((Resource)resource).ComputeParameterFingerprint(varied),
        };
    }

    protected internal override void ApplyRequestInputs(GenerativeRequest request)
    {
        if (request is AiImageGenerationNodeRequest { Seed: int seed } && Seed.Connection.IsNull)
            Seed.Property?.SetValue(seed);
    }

    protected internal override void ApplyRecordInputs(GenerationRecord record)
    {
        if (record.Seed is int seed && Seed.Connection.IsNull)
            Seed.Property?.SetValue(seed);
    }

    public partial class Resource
    {
        private (string?, string?, string?, string?, int?) _lastParameters;
        private string? _lastParameterFingerprint;

        public override void Update(GraphCompositionContext context)
        {
            var node = RequireOriginal();
            Output = UpdateActiveImageOutput(context);
            node.ReportParameterFingerprint(ComputeParameterFingerprint());
        }

        internal string ComputeParameterFingerprint() => ComputeParameterFingerprint(null);

        internal string ComputeParameterFingerprint(int? seedOverride)
        {
            var parameters = (Prompt?.Trim(), Model?.Trim(), AspectRatio?.Trim(), Background?.Trim(), seedOverride ?? SeedFor(this));
            if (seedOverride is null && _lastParameterFingerprint is not null && parameters == _lastParameters)
                return _lastParameterFingerprint;

            string fingerprint = GenerativeFingerprint.Combine(
            [
                parameters.Item1,
                parameters.Item2,
                parameters.Item3,
                parameters.Item4,
                parameters.Item5?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ]);
            if (seedOverride is null)
            {
                _lastParameters = parameters;
                _lastParameterFingerprint = fingerprint;
            }

            return fingerprint;
        }
    }
}
