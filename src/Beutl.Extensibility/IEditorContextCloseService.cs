namespace Beutl.Extensibility;

/// <summary>Requests host-owned editor-context closure without synchronously joining teardown.</summary>
public interface IEditorContextCloseService
{
    /// <summary>
    /// Gets the opaque host identity for this close capability.
    /// </summary>
    /// <remarks>
    /// A context must retain the capability supplied by its creating host, including this token.
    /// Hosts compare the token by reference when attaching or replacing a context so a context
    /// cannot route close requests to a different host. Implementations must return a stable,
    /// non-null token for the lifetime of the capability and acquire an ownership lease before
    /// publishing each context.
    /// </remarks>
    EditorContextHostToken HostToken { get; }

    /// <summary>Requests closure of the tab that owns <paramref name="context"/>.</summary>
    /// <remarks>
    /// Observer and dispatcher callbacks should retain or ignore the returned completion instead of
    /// synchronously waiting for it. Normal disposal remains available through
    /// <see cref="IEditorContext.DisposeAsync"/>.
    /// </remarks>
    EditorContextCloseRequest RequestClose(IEditorContext context);
}

/// <summary>The result of requesting host-owned editor-context closure.</summary>
/// <remarks>
/// For <see cref="EditorContextCloseRequestStatus.Accepted"/> and
/// <see cref="EditorContextCloseRequestStatus.AlreadyClosing"/>, <see cref="Completion"/> covers
/// physical tab removal and context teardown and propagates their failures. A
/// <see cref="EditorContextCloseRequestStatus.NotOwned"/> request is a completed no-op. The default
/// value is also a valid <c>NotOwned</c> result.
/// </remarks>
public readonly struct EditorContextCloseRequest
{
    private readonly Task? _completion;

    public EditorContextCloseRequest(
        EditorContextCloseRequestStatus status,
        Task completion)
    {
        ArgumentNullException.ThrowIfNull(completion);
        Status = status;
        _completion = completion;
    }

    public EditorContextCloseRequestStatus Status { get; }

    public Task Completion => _completion ?? Task.CompletedTask;
}

/// <summary>Describes whether an editor-context close request was accepted by its host.</summary>
public enum EditorContextCloseRequestStatus
{
    /// <summary>The context is not owned by this host.</summary>
    NotOwned,

    /// <summary>A new terminal close was accepted.</summary>
    Accepted,

    /// <summary>The context was already closing; <see cref="EditorContextCloseRequest.Completion"/> joins it.</summary>
    AlreadyClosing
}
