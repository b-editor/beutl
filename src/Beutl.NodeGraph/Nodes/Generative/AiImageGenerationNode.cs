using Beutl.Extensibility;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.NodeGraph.Composition;
using Beutl.NodeGraph.Generative;

namespace Beutl.NodeGraph.Nodes.Generative;

/// <summary>Generates a picture from a prompt and optional reference pictures.</summary>
public sealed partial class AiImageGenerationNode : SeededGenerativeNode, IPromptInputNode
{
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

    protected internal override GenerativeRequest BuildRequest(GraphNode.Resource resource, GraphCompositionContext context)
    {
        var r = (Resource)resource;
        if (string.IsNullOrWhiteSpace(r.Prompt))
            throw new GenerativeExecutionException(NodeGraphStrings.Generative_PromptRequired);

        List<RenderNode?> referenceNodes = context.CollectListInputValues(References);
        var references = new List<GenerativeImageInput>(referenceNodes.Count);
        AddRasterizedReferences(references, referenceNodes, context);

        return new AiImageGenerationNodeRequest(this)
        {
            Prompt = r.Prompt!.Trim(),
            AspectRatio = string.IsNullOrWhiteSpace(r.AspectRatio) ? DefaultAspectRatio : r.AspectRatio!.Trim(),
            Background = string.IsNullOrWhiteSpace(r.Background) ? "auto" : r.Background!.Trim(),
            Seed = r.RequestSeed,
            ModelId = NormalizeModelId(r.Model),
            References = references,
            RequestKeySeed = RequestKeySeed.Value,
            ParameterFingerprint = r.ComputeParameterFingerprint(),
        };
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

        internal override string ComputeParameterFingerprint(int? seedOverride)
        {
            var parameters = (Prompt?.Trim(), Model?.Trim(), AspectRatio?.Trim(), Background?.Trim(), seedOverride ?? RequestSeed);
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
