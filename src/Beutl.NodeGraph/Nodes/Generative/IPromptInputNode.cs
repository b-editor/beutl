using Beutl.NodeGraph.Generative;

namespace Beutl.NodeGraph.Nodes.Generative;

/// <summary>A generation node whose prompt input is what the prompt library fills and saves.</summary>
internal interface IPromptInputNode : IPromptLibraryTarget
{
    GenerativeOperation Operation { get; }

    InputPort<string> Prompt { get; }

    GenerativeOperation IPromptLibraryTarget.PromptOperation => Operation;

    bool IPromptLibraryTarget.CanApplyPrompt => Prompt.Connection.IsNull;

    string IPromptLibraryTarget.ComposePrompt() => Prompt.Property?.GetValue()?.Trim() ?? string.Empty;

    void IPromptLibraryTarget.ApplyPrompt(string prompt) => Prompt.Property?.SetValue(prompt);
}
