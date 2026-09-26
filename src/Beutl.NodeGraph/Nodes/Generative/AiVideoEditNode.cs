using System.Globalization;
using Beutl.Extensibility;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.Media.Source;
using Beutl.NodeGraph.Composition;
using Beutl.NodeGraph.Generative;

namespace Beutl.NodeGraph.Nodes.Generative;

/// <summary>
/// Edits, extends or drives the motion of a clip with the AI tab's video editing tasks.
/// Any clip in the graph can be the source — including one a video node just generated.
/// </summary>
public sealed partial class AiVideoEditNode : GenerativeNode, IPromptLibraryTarget
{
    public AiVideoEditNode()
    {
        Output = AddOutput<VideoSource?>("Video", NodePortDisplays.Video);
        // In the AI tab's video editing page order.
        Task = AddInput<AiVideoEditMode>("Task", NodePortDisplays.Task);
        Source = AddInput<VideoSource?>("Source", NodePortDisplays.Source);
        Model = AddInput<string>("Model", NodePortDisplays.Model);
        Prompt = AddInput<string>("Prompt", NodePortDisplays.Prompt);
        Duration = AddInput<int>("Duration", NodePortDisplays.Duration);
        CharacterImage = AddInput<RenderNode?>("CharacterImage", NodePortDisplays.CharacterImage);
        Orientation = AddInput<AiMotionOrientation>("Orientation", NodePortDisplays.Orientation);
        Quality = AddInput<AiMotionQuality>("Quality", NodePortDisplays.Quality);
        AddGenerativeMonitors(NodePortDisplays.Preview, NodePortDisplays.Status);

        Model.Property?.SetValue(string.Empty);
        Prompt.Property?.SetValue(string.Empty);
        Duration.Property?.SetValue(GenerativeVideoCapabilities.DefaultDuration);
        UseMultilineEditor(Prompt);
        RegisterChoice(Model, GenerativeChoiceKind.Model);
        RegisterChoice(Duration, GenerativeChoiceKind.Duration);
        Task.Property?.GetObservable().Subscribe(_ => RaiseCatalogOperationChanged());
    }

    public override GenerativeOperation Operation => GenerativeOperation.VideoEdit;

    public override string CatalogOperationId => (Task.Property?.GetValue() ?? AiVideoEditMode.Edit) switch
    {
        AiVideoEditMode.Extend => "video.extend",
        AiVideoEditMode.Motion => "video.motion",
        _ => "video.edit",
    };

    public override IPropertyAdapter<string>? ModelProperty => Model.Property;

    public OutputPort<VideoSource?> Output { get; }

    public InputPort<AiVideoEditMode> Task { get; }

    public InputPort<VideoSource?> Source { get; }

    public InputPort<string> Model { get; }

    public InputPort<string> Prompt { get; }

    public InputPort<int> Duration { get; }

    public InputPort<RenderNode?> CharacterImage { get; }

    public InputPort<AiMotionOrientation> Orientation { get; }

    public InputPort<AiMotionQuality> Quality { get; }

    public GenerativeOperation PromptOperation => Operation;

    public bool CanApplyPrompt => Prompt.Connection.IsNull;

    public string ComposePrompt() => Prompt.Property?.GetValue()?.Trim() ?? string.Empty;

    public void ApplyPrompt(string prompt) => Prompt.Property?.SetValue(prompt);

    protected internal override GenerativeRequest BuildRequest(GraphNode.Resource resource, GraphCompositionContext context)
    {
        var r = (Resource)resource;
        string prompt = r.Prompt?.Trim() ?? string.Empty;
        if (prompt.Length == 0)
            throw new GenerativeExecutionException(Strings.AiPromptRequired);
        GenerativeFileInput source = ReadVideoInput(r.Source, "source")
            ?? throw new GenerativeExecutionException(Strings.AiVideoNoSourceSelected);
        GenerativeImageInput? character = null;
        if (r.Task == AiVideoEditMode.Motion)
        {
            character = RasterizeInput(r.CharacterImage, "character", context)
                ?? throw new GenerativeExecutionException(Strings.AiChooseCharacterImage);
        }

        return new AiVideoEditNodeRequest(this)
        {
            Mode = r.Task,
            Prompt = prompt,
            DurationSeconds = r.Duration,
            SourceVideo = source,
            CharacterImage = character,
            Orientation = r.Orientation,
            Quality = r.Quality,
            ModelId = string.IsNullOrWhiteSpace(r.Model) ? null : r.Model!.Trim(),
            RequestKeySeed = RequestKeySeed,
            ParameterFingerprint = r.ComputeParameterFingerprint(),
        };
    }

    public partial class Resource
    {
        private (AiVideoEditMode, string?, string?, int, AiMotionOrientation, AiMotionQuality) _lastParameters;
        private string? _lastParameterFingerprint;

        public override void Update(GraphCompositionContext context)
        {
            Output = UpdateActiveVideoOutput(context);
            RequireOriginal().ReportParameterFingerprint(ComputeParameterFingerprint());
        }

        internal string ComputeParameterFingerprint()
        {
            bool motion = Task == AiVideoEditMode.Motion;
            var parameters = (Task, Prompt?.Trim(), Model?.Trim(), Task == AiVideoEditMode.Edit ? 0 : Duration,
                motion ? Orientation : AiMotionOrientation.Video, motion ? Quality : AiMotionQuality.Standard);
            if (_lastParameterFingerprint is not null && parameters == _lastParameters)
                return _lastParameterFingerprint;

            _lastParameters = parameters;
            return _lastParameterFingerprint = GenerativeFingerprint.Combine(
            [
                parameters.Item1.ToString(),
                parameters.Item2,
                parameters.Item3,
                parameters.Item4.ToString(CultureInfo.InvariantCulture),
                parameters.Item5.ToString(),
                parameters.Item6.ToString(),
            ]);
        }
    }
}
