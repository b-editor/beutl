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
        Motion = AddInput<string>("Motion", NodePortDisplays.Motion);
        Exclusions = AddInput<string>("Exclusions", NodePortDisplays.Exclusions);
        Prompt.Property?.SetValue(string.Empty);
        Style.Property?.SetValue(string.Empty);
        Composition.Property?.SetValue(string.Empty);
        Exclusions.Property?.SetValue(string.Empty);
        Motion.Property?.SetValue(string.Empty);
        UseMultilineEditor(Prompt);
        UseMultilineEditor(Motion);
        UseMultilineEditor(Style);
        UseMultilineEditor(Composition);
        UseMultilineEditor(Exclusions);
    }

    public OutputPort<string> Output { get; }

    public InputPort<string> Prompt { get; }

    public InputPort<string> Style { get; }

    public InputPort<string> Composition { get; }

    /// <summary>How things move; only video models read it, as in the AI tab's video page.</summary>
    public InputPort<string> Motion { get; }

    public InputPort<string> Exclusions { get; }

    public GenerativeOperation PromptOperation => GenerativeOperation.ImageGeneration;

    // Applying sets the fields' own values, which a connected field would ignore.
    public bool CanApplyPrompt => Prompt.Connection.IsNull && Style.Connection.IsNull
        && Composition.Connection.IsNull && Motion.Connection.IsNull && Exclusions.Connection.IsNull;

    public string ComposePrompt() => PromptSections.Compose(
        Prompt.Property?.GetValue(),
        Style.Property?.GetValue(),
        Composition.Property?.GetValue(),
        Motion.Property?.GetValue(),
        Exclusions.Property?.GetValue());

    /// <summary>
    /// A saved prompt is already written out with its sections, so it is split back into the
    /// fields: composing them again sends the saved text, not a second copy of its labels.
    /// </summary>
    public void ApplyPrompt(string prompt)
    {
        (string main, string style, string composition, string motion, string exclusions) = PromptSections.Parse(prompt);
        Prompt.Property?.SetValue(main);
        Style.Property?.SetValue(style);
        Composition.Property?.SetValue(composition);
        Motion.Property?.SetValue(motion);
        Exclusions.Property?.SetValue(exclusions);
    }

    public partial class Resource
    {
        private (string?, string?, string?, string?, string?) _last;
        private string? _composed;

        public override void Update(GraphCompositionContext context)
        {
            var fields = (Prompt, Style, Composition, Motion, Exclusions);
            if (_composed is null || fields != _last)
            {
                _last = fields;
                _composed = PromptSections.Compose(Prompt, Style, Composition, Motion, Exclusions);
            }

            Output = _composed;
        }
    }
}
