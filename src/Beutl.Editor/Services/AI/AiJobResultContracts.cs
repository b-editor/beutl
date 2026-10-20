using System.Diagnostics.CodeAnalysis;
using Beutl.Api.Services;
using Beutl.Extensibility;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Services.AI;

/// <summary>
/// Provides the scene-editing capabilities required to apply an AI job result without coupling
/// result applicators to a concrete desktop view model.
/// </summary>
public interface IAiJobResultEditorContext : IEditorContext
{
    Scene Scene { get; }

    TimeSpan CurrentTime { get; }

    IElementAdder ElementAdder { get; }

    int GetNextLayer(TimeSpan start);
}

/// <summary>
/// Provides the editor-specific dependencies needed to apply an AI job result.
/// </summary>
public interface IAiJobResultContext
{
    IAiJobResultEditorContext Editor { get; }

    Task<AiContentDownload> CopyContentToAsync(
        Uri contentUri,
        Stream destination,
        CancellationToken cancellationToken);
}

/// <summary>
/// Describes an AI job for editor UI surfaces.
/// </summary>
/// <param name="HasImagePreview">
/// Whether the job's content is a still picture the job list can show. Only the
/// presenter knows what its kind produces, and a list of prompts is far harder to
/// search than a list of pictures.
/// </param>
public sealed record AiJobPresentation(
    string KindDisplayName,
    string StatusDisplayName,
    string Summary,
    string Details,
    bool IsFailure,
    bool HasImagePreview = false);

/// <summary>The notification severity for a terminal AI job.</summary>
public enum AiJobNotificationKind
{
    Information,
    Success,
    Warning,
}

/// <summary>Describes an editor notification for a terminal AI job.</summary>
public sealed record AiJobCompletionPresentation(
    string Title,
    string Message,
    AiJobNotificationKind Notification,
    TimeSpan? Expiration = null);

/// <summary>Provides list presentation for one AI job kind.</summary>
public interface IAiJobPresenter
{
    AiJobPresentation Present(AiJob job, AiJobStatusSemantics status);
}

/// <summary>Provides completion notifications for one AI job kind.</summary>
public interface IAiJobCompletionPresenter
{
    AiJobCompletionPresentation? CreateCompletion(
        AiJob job,
        AiJobStatusSemantics status);
}

/// <summary>Applies successful results for one AI job kind to an editor.</summary>
public interface IAiJobResultApplicator
{
    bool CanApply(AiJob job, AiJobStatusSemantics status);

    Task ApplyAsync(
        AiJob job,
        IAiJobResultContext context,
        CancellationToken cancellationToken);
}

// The presenters, completion presenters and result applicators of Beutl's AI job kinds.
public sealed class AiJobResultRegistry
{
    private readonly Dictionary<string, IAiJobPresenter> _presenters;
    private readonly Dictionary<string, IAiJobCompletionPresenter> _completionPresenters;
    private readonly Dictionary<string, IAiJobResultApplicator> _applicators;

    internal AiJobResultRegistry(
        IEnumerable<KeyValuePair<AiJobKindId, IAiJobPresenter>> presenters,
        IEnumerable<KeyValuePair<AiJobKindId, IAiJobCompletionPresenter>> completionPresenters,
        IEnumerable<KeyValuePair<AiJobKindId, IAiJobResultApplicator>> applicators)
    {
        _presenters = ToDictionary(presenters);
        _completionPresenters = ToDictionary(completionPresenters);
        _applicators = ToDictionary(applicators);
    }

    public bool TryGetPresenter(
        AiJobKindId kind,
        [NotNullWhen(true)] out IAiJobPresenter? presenter)
        => _presenters.TryGetValue(kind.Value, out presenter);

    public bool TryGetCompletionPresenter(
        AiJobKindId kind,
        [NotNullWhen(true)] out IAiJobCompletionPresenter? presenter)
        => _completionPresenters.TryGetValue(kind.Value, out presenter);

    public bool TryGetApplicator(
        AiJobKindId kind,
        [NotNullWhen(true)] out IAiJobResultApplicator? applicator)
        => _applicators.TryGetValue(kind.Value, out applicator);

    // Kinds match without regard to case, as in AiJobKindRegistry.
    private static Dictionary<string, T> ToDictionary<T>(IEnumerable<KeyValuePair<AiJobKindId, T>> pairs)
        => pairs.ToDictionary(pair => pair.Key.Value, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
}
