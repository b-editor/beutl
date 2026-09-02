namespace Beutl.Extensibility;

/// <summary>Requests host-owned editor-context closure without synchronously joining teardown.</summary>
public interface IEditorContextCloseService
{
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
