using Beutl.Extensibility;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.NodeGraph.Composition;
using Beutl.NodeGraph.Generative;

namespace Beutl.NodeGraph.Nodes.Generative;

/// <summary>Edits a picture with the AI tab's edit tasks. Any picture in the graph can be the source.</summary>
public sealed partial class AiImageEditNode : GenerativeNode, IPromptLibraryTarget
{
    public AiImageEditNode()
    {
        Output = AddOutput<ImageSourceRenderNode?>("Image", NodePortDisplays.Image);
        // In the AI tab's order.
        Source = AddInput<RenderNode?>("Source", NodePortDisplays.Source);
        Task = AddInput<AiImageEditTask>("Task", NodePortDisplays.Task);
        Model = AddInput<string>("Model", NodePortDisplays.Model);
        Prompt = AddInput<string>("Prompt", NodePortDisplays.Prompt);
        OutpaintExpansion = AddInput<AiOutpaintExpansion>("OutpaintExpansion", NodePortDisplays.OutpaintExpansion);
        AddGenerativeMonitors(NodePortDisplays.Preview, NodePortDisplays.Status);

        Model.Property?.SetValue(string.Empty);
        Prompt.Property?.SetValue(string.Empty);
        OutpaintExpansion.Property?.SetValue(AiOutpaintExpansion.Percent25);
        UseMultilineEditor(Prompt);
        RegisterChoice(Model, GenerativeChoiceKind.Model);
        Task.Property?.GetObservable().Subscribe(_ => RaiseCatalogOperationChanged());
    }

    public override GenerativeOperation Operation => GenerativeOperation.ImageEdit;

    public override string CatalogOperationId
        => $"image.edit.{(Task.Property?.GetValue() ?? AiImageEditTask.RemoveBackground).ToId()}";

    public override IPropertyAdapter<string>? ModelProperty => Model.Property;

    public OutputPort<ImageSourceRenderNode?> Output { get; }

    public InputPort<RenderNode?> Source { get; }

    public InputPort<AiImageEditTask> Task { get; }

    public InputPort<string> Model { get; }

    public InputPort<string> Prompt { get; }

    public InputPort<AiOutpaintExpansion> OutpaintExpansion { get; }

    public GenerativeOperation PromptOperation => Operation;

    public bool CanApplyPrompt => Prompt.Connection.IsNull;

    public string ComposePrompt() => Prompt.Property?.GetValue()?.Trim() ?? string.Empty;

    public void ApplyPrompt(string prompt) => Prompt.Property?.SetValue(prompt);

    protected internal override GenerativeRequest BuildRequest(GraphNode.Resource resource, GraphCompositionContext context)
    {
        var r = (Resource)resource;
        AiImageEditTask task = r.Task;
        string? prompt = task.RequiresPrompt() ? r.Prompt?.Trim() : null;
        if (task.RequiresPrompt() && string.IsNullOrEmpty(prompt))
            throw new GenerativeExecutionException(Strings.AiPromptRequired);
        GenerativeImageInput image = RasterizeInput(r.Source, "image", context)
            ?? throw new GenerativeExecutionException(Strings.AiEditSelectSource);

        return new AiImageEditNodeRequest(this)
        {
            Task = task,
            Prompt = prompt,
            OutpaintExpansionPercent = task == AiImageEditTask.Outpaint ? r.OutpaintExpansion.ToPercent() : null,
            Image = image,
            ModelId = string.IsNullOrWhiteSpace(r.Model) ? null : r.Model!.Trim(),
            RequestKeySeed = RequestKeySeed,
            ParameterFingerprint = r.ComputeParameterFingerprint(),
        };
    }

    public partial class Resource
    {
        private (AiImageEditTask, string?, string?, AiOutpaintExpansion) _lastParameters;
        private string? _lastParameterFingerprint;

        public override void Update(GraphCompositionContext context)
        {
            Output = UpdateActiveImageOutput(context);
            RequireOriginal().ReportParameterFingerprint(ComputeParameterFingerprint());
        }

        internal string ComputeParameterFingerprint()
        {
            var parameters = (Task, Task.RequiresPrompt() ? Prompt?.Trim() : null, Model?.Trim(),
                Task == AiImageEditTask.Outpaint ? OutpaintExpansion : AiOutpaintExpansion.Percent25);
            if (_lastParameterFingerprint is not null && parameters == _lastParameters)
                return _lastParameterFingerprint;

            _lastParameters = parameters;
            return _lastParameterFingerprint = GenerativeFingerprint.Combine(
            [
                parameters.Item1.ToId(),
                parameters.Item2,
                parameters.Item3,
                parameters.Item4.ToPercent().ToString(System.Globalization.CultureInfo.InvariantCulture),
            ]);
        }
    }
}
