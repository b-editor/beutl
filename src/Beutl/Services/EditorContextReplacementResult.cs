namespace Beutl.Services;

/// <summary>Describes the result of replacing an editor tab's context.</summary>
internal readonly struct EditorContextReplacementResult
{
    internal EditorContextReplacementResult(
        EditorContextReplacementStatus status,
        bool inputConsumed)
    {
        Status = status;
        InputConsumed = inputConsumed;
    }

    /// <summary>Gets the replacement outcome.</summary>
    internal EditorContextReplacementStatus Status { get; }

    /// <summary>
    /// Gets a value indicating whether the replacement operation consumed the supplied context.
    /// </summary>
    internal bool InputConsumed { get; }

    /// <summary>Gets a value indicating whether the context was accepted by the tab.</summary>
    internal bool Succeeded => Status == EditorContextReplacementStatus.Succeeded;
}

/// <summary>Describes why an editor-context replacement completed.</summary>
public enum EditorContextReplacementStatus
{
    /// <summary>The extension did not create a context.</summary>
    CreationFailed,

    /// <summary>The context was published and ownership transferred to the tab.</summary>
    Succeeded,

    /// <summary>The tab is not owned by this host, or the factory result already belongs to another owner.</summary>
    NotOwned,

    /// <summary>The factory returned the context that is already active in this tab.</summary>
    AlreadyActive,

    /// <summary>The tab changed, is closing, or has another replacement in progress.</summary>
    Busy
}
