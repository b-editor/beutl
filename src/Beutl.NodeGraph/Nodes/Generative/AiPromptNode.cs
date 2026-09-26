using Beutl.NodeGraph.Composition;
using Beutl.NodeGraph.Generative;

namespace Beutl.NodeGraph.Nodes.Generative;

/// <summary>
/// Builds a prompt from the same fields as the AI dialogs. One prompt node can feed
/// several generation nodes, and its fields can be driven by other nodes.
/// </summary>
public sealed partial class AiPromptNode : GraphNode, IPromptLibraryTarget
{
    public AiPromptNode()
    {
        Output = AddOutput<string>("Output", NodePortDisplays.Prompt);
        Prompt = AddInput<string>("Prompt", NodePortDisplays.Prompt);
        Style = AddInput<string>("Style", NodePortDisplays.Style);
        Composition = AddInput<string>("Composition", NodePortDisplays.Composition);
        Exclusions = AddInput<string>("Exclusions", NodePortDisplays.Exclusions);
        Prompt.Property?.SetValue(string.Empty);
        Style.Property?.SetValue(string.Empty);
        Composition.Property?.SetValue(string.Empty);
        Exclusions.Property?.SetValue(string.Empty);
    }

    public OutputPort<string> Output { get; }

    public InputPort<string> Prompt { get; }

    public InputPort<string> Style { get; }

    public InputPort<string> Composition { get; }

    public InputPort<string> Exclusions { get; }

    public GenerativeOperation PromptOperation => GenerativeOperation.ImageGeneration;

    public bool CanApplyPrompt => true;

    public string ComposePrompt() => PromptSections.Compose(
        Prompt.Property?.GetValue(),
        Style.Property?.GetValue(),
        Composition.Property?.GetValue(),
        exclusions: Exclusions.Property?.GetValue());

    /// <summary>
    /// A saved prompt is already written out with its sections, so it replaces the main
    /// field and clears the others rather than being composed with them a second time.
    /// </summary>
    public void ApplyPrompt(string prompt)
    {
        Prompt.Property?.SetValue(prompt);
        Style.Property?.SetValue(string.Empty);
        Composition.Property?.SetValue(string.Empty);
        Exclusions.Property?.SetValue(string.Empty);
    }

    public partial class Resource
    {
        private (string?, string?, string?, string?) _last;
        private string? _composed;

        public override void Update(GraphCompositionContext context)
        {
            var fields = (Prompt, Style, Composition, Exclusions);
            if (_composed is null || fields != _last)
            {
                _last = fields;
                _composed = PromptSections.Compose(Prompt, Style, Composition, exclusions: Exclusions);
            }

            Output = _composed;
        }
    }
}
