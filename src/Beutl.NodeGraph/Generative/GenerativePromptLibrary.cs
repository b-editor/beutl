namespace Beutl.NodeGraph.Generative;

/// <summary>A saved prompt: a named template or one from the account's history.</summary>
public sealed record GenerativePromptEntry(string Name, string Prompt, bool IsTemplate, bool IsPinned);

/// <summary>
/// The account's prompt library, shared with the AI dialogs. Implemented by the application.
/// </summary>
public interface IGenerativePromptLibrary
{
    /// <summary>Templates, then history; pinned first, newest first, as the dialogs list them.</summary>
    IReadOnlyList<GenerativePromptEntry> GetEntries(GenerativeOperation operation);

    void Record(GenerativeOperation operation, string prompt);

    /// <summary>Saves a template; returns a message for the user when it could not.</summary>
    string? SaveTemplate(GenerativeOperation operation, string name, string prompt);
}

/// <summary>A node whose prompt can be filled from, or saved to, the prompt library.</summary>
public interface IPromptLibraryTarget
{
    GenerativeOperation PromptOperation { get; }

    /// <summary>False while the prompt comes from a connection, where a typed value has no effect.</summary>
    bool CanApplyPrompt { get; }

    /// <summary>The prompt as it would be sent.</summary>
    string ComposePrompt();

    /// <summary>Replaces the node's prompt with a saved one.</summary>
    void ApplyPrompt(string prompt);
}
