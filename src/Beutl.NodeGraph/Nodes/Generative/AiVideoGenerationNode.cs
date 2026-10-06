using System.Globalization;
using Beutl.Extensibility;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.Media.Source;
using Beutl.NodeGraph.Composition;
using Beutl.NodeGraph.Generative;

namespace Beutl.NodeGraph.Nodes.Generative;

/// <summary>
/// Generates a clip from a prompt, optionally guided by first and last frames or by
/// reference pictures and clips. Any picture in the graph can be a frame or a reference.
/// </summary>
public sealed partial class AiVideoGenerationNode : GenerativeNode, IPromptLibraryTarget
{
    public AiVideoGenerationNode()
    {
        Output = AddOutput<VideoSource?>("Video", NodePortDisplays.Video);
        // In the AI tab's video page order.
        Prompt = AddInput<string>("Prompt", NodePortDisplays.Prompt);
        Model = AddInput<string>("Model", NodePortDisplays.Model);
        Duration = AddInput<int>("Duration", NodePortDisplays.Duration);
        Resolution = AddInput<string>("Resolution", NodePortDisplays.Resolution);
        AspectRatio = AddInput<string>("AspectRatio", NodePortDisplays.AspectRatio);
        GenerateAudio = AddInput<bool>("GenerateAudio", NodePortDisplays.GenerateAudio);
        Seed = AddInput<int>("Seed", NodePortDisplays.Seed);
        SeedControl = AddInput<GenerativeSeedControl>("SeedControl", NodePortDisplays.SeedControl);
        FirstFrame = AddInput<RenderNode?>("FirstFrame", NodePortDisplays.FirstFrame);
        LastFrame = AddInput<RenderNode?>("LastFrame", NodePortDisplays.LastFrame);
        ImageReferences = AddListInput<RenderNode?>("ImageReferences", NodePortDisplays.ImageReferences);
        VideoReferences = AddListInput<VideoSource?>("VideoReferences", NodePortDisplays.VideoReferences);
        AddGenerativeMonitors(NodePortDisplays.Preview, NodePortDisplays.Status);

        Prompt.Property?.SetValue(string.Empty);
        Model.Property?.SetValue(string.Empty);
        Duration.Property?.SetValue(GenerativeVideoCapabilities.DefaultDuration);
        Resolution.Property?.SetValue(GenerativeVideoCapabilities.DefaultResolutions[0]);
        AspectRatio.Property?.SetValue(GenerativeVideoCapabilities.DefaultAspectRatios[0]);
        GenerateAudio.Property?.SetValue(true);
        Seed.Property?.SetValue(Random.Shared.Next(AiImageGenerationNode.MinSeed, AiImageGenerationNode.MaxSeed));
        UseMultilineEditor(Prompt);
        RegisterChoice(Model, GenerativeChoiceKind.Model);
        RegisterChoice(Duration, GenerativeChoiceKind.Duration);
        RegisterChoice(Resolution, GenerativeChoiceKind.Resolution);
        RegisterChoice(AspectRatio, GenerativeChoiceKind.AspectRatio);
    }

    public override GenerativeOperation Operation => GenerativeOperation.VideoGeneration;

    public override string CatalogOperationId => "video.generate";

    public override IPropertyAdapter<string>? ModelProperty => Model.Property;

    public OutputPort<VideoSource?> Output { get; }

    public InputPort<string> Prompt { get; }

    public InputPort<string> Model { get; }

    public InputPort<int> Duration { get; }

    public InputPort<string> Resolution { get; }

    public InputPort<string> AspectRatio { get; }

    public InputPort<bool> GenerateAudio { get; }

    public InputPort<int> Seed { get; }

    public InputPort<GenerativeSeedControl> SeedControl { get; }

    public InputPort<RenderNode?> FirstFrame { get; }

    public InputPort<RenderNode?> LastFrame { get; }

    public ListInputPort<RenderNode?> ImageReferences { get; }

    public ListInputPort<VideoSource?> VideoReferences { get; }

    public GenerativeOperation PromptOperation => Operation;

    public bool CanApplyPrompt => Prompt.Connection.IsNull;

    public string ComposePrompt() => Prompt.Property?.GetValue()?.Trim() ?? string.Empty;

    public void ApplyPrompt(string prompt) => Prompt.Property?.SetValue(prompt);

    protected internal override GenerativeRequest BuildRequest(GraphNode.Resource resource, GraphCompositionContext context)
    {
        var r = (Resource)resource;
        // The AI tab asks for a prompt whatever guides the clip.
        if (string.IsNullOrWhiteSpace(r.Prompt))
            throw new GenerativeExecutionException(Strings.AiPromptRequired);
        GenerativeImageInput? first = RasterizeInput(r.FirstFrame, "first-frame", context);
        GenerativeImageInput? last = RasterizeInput(r.LastFrame, "last-frame", context);
        if (last is not null && first is null)
        {
            // The endpoint takes no last frame without a first one.
            throw new GenerativeExecutionException(Strings.AiModelDoesNotSupportRequest);
        }

        var images = new List<GenerativeImageInput>();
        var videos = new List<GenerativeFileInput>();
        // As in the AI tab, references are set aside while a first frame guides the clip.
        if (first is null)
        {
            List<RenderNode?> imageNodes = context.CollectListInputValues(ImageReferences);
            AddRasterizedReferences(images, imageNodes, context);

            List<VideoSource?> videoSources = context.CollectListInputValues(VideoReferences);
            for (int i = 0; i < videoSources.Count; i++)
            {
                if (ReadVideoInput(videoSources[i], $"reference-{i + 1}") is { } video)
                    videos.Add(video);
            }
        }

        return new AiVideoGenerationNodeRequest(this)
        {
            Prompt = r.Prompt?.Trim() ?? string.Empty,
            DurationSeconds = r.Duration,
            Resolution = string.IsNullOrWhiteSpace(r.Resolution) ? GenerativeVideoCapabilities.DefaultResolutions[0] : r.Resolution!.Trim(),
            AspectRatio = string.IsNullOrWhiteSpace(r.AspectRatio) ? GenerativeVideoCapabilities.DefaultAspectRatios[0] : r.AspectRatio!.Trim(),
            GenerateAudio = r.GenerateAudio,
            Seed = r.SeedControl == GenerativeSeedControl.ModelDefault ? null : r.Seed,
            FirstFrame = first,
            LastFrame = last,
            ImageReferences = images,
            VideoReferences = videos,
            ModelId = NormalizeModelId(r.Model),
            RequestKeySeed = RequestKeySeed,
            ParameterFingerprint = r.ComputeParameterFingerprint(),
        };
    }

    protected internal override void OnGenerated(GenerationRecord record)
        => AiImageGenerationNode.AdvanceSeed(Seed, SeedControl);

    protected internal override GenerativeRequest BuildVariation(
        GraphNode.Resource resource,
        GraphCompositionContext context,
        int index)
    {
        var request = (AiVideoGenerationNodeRequest)BuildRequest(resource, context);
        if (index == 0 || request.Seed is not int seed)
            return request;

        int varied = AiImageGenerationNode.VarySeed(seed, index);
        return request with
        {
            Seed = varied,
            ParameterFingerprint = ((Resource)resource).ComputeParameterFingerprint(varied),
        };
    }

    protected internal override void ApplyRequestInputs(GenerativeRequest request)
    {
        AiImageGenerationNode.ApplySeedInput(Seed, (request as AiVideoGenerationNodeRequest)?.Seed);
    }

    protected internal override void ApplyRecordInputs(GenerationRecord record)
    {
        AiImageGenerationNode.ApplySeedInput(Seed, record.Seed);
    }

    public partial class Resource
    {
        private (string?, string?, int, string?, string?, bool, int?) _lastParameters;
        private string? _lastParameterFingerprint;

        public override void Update(GraphCompositionContext context)
        {
            Output = UpdateActiveVideoOutput(context);
            RequireOriginal().ReportParameterFingerprint(ComputeParameterFingerprint());
        }

        internal string ComputeParameterFingerprint() => ComputeParameterFingerprint(null);

        internal string ComputeParameterFingerprint(int? seedOverride)
        {
            var parameters = (Prompt?.Trim(), Model?.Trim(), Duration, Resolution?.Trim(), AspectRatio?.Trim(),
                GenerateAudio, seedOverride ?? (SeedControl == GenerativeSeedControl.ModelDefault ? (int?)null : Seed));
            if (seedOverride is null && _lastParameterFingerprint is not null && parameters == _lastParameters)
                return _lastParameterFingerprint;

            string fingerprint = GenerativeFingerprint.Combine(
            [
                parameters.Item1,
                parameters.Item2,
                parameters.Item3.ToString(CultureInfo.InvariantCulture),
                parameters.Item4,
                parameters.Item5,
                parameters.Item6 ? "audio" : "silent",
                parameters.Item7?.ToString(CultureInfo.InvariantCulture),
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
