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

/// <summary>Identifies one independently registered editor-side AI result capability.</summary>
public enum AiJobResultCapability
{
    Presentation,
    Completion,
    Application,
}

/// <summary>Registers list presentation for one AI job kind.</summary>
public sealed class AiJobPresentationRegistration
{
    public AiJobPresentationRegistration(
        AiJobKindId kind,
        IAiJobPresenter presenter,
        AiJobBehaviorRegistrationMode mode = AiJobBehaviorRegistrationMode.Add)
    {
        AiJobResultContractValidation.Validate(kind, mode);
        Kind = kind;
        Presenter = presenter ?? throw new ArgumentNullException(nameof(presenter));
        Mode = mode;
    }

    public AiJobKindId Kind { get; }

    public IAiJobPresenter Presenter { get; }

    public AiJobBehaviorRegistrationMode Mode { get; }
}

/// <summary>Registers completion notification presentation for one AI job kind.</summary>
public sealed class AiJobCompletionRegistration
{
    public AiJobCompletionRegistration(
        AiJobKindId kind,
        IAiJobCompletionPresenter presenter,
        AiJobBehaviorRegistrationMode mode = AiJobBehaviorRegistrationMode.Add)
    {
        AiJobResultContractValidation.Validate(kind, mode);
        Kind = kind;
        Presenter = presenter ?? throw new ArgumentNullException(nameof(presenter));
        Mode = mode;
    }

    public AiJobKindId Kind { get; }

    public IAiJobCompletionPresenter Presenter { get; }

    public AiJobBehaviorRegistrationMode Mode { get; }
}

/// <summary>Registers result application for one AI job kind.</summary>
public sealed class AiJobResultApplicatorRegistration
{
    public AiJobResultApplicatorRegistration(
        AiJobKindId kind,
        IAiJobResultApplicator applicator,
        AiJobBehaviorRegistrationMode mode = AiJobBehaviorRegistrationMode.Add)
    {
        AiJobResultContractValidation.Validate(kind, mode);
        Kind = kind;
        Applicator = applicator ?? throw new ArgumentNullException(nameof(applicator));
        Mode = mode;
    }

    public AiJobKindId Kind { get; }

    public IAiJobResultApplicator Applicator { get; }

    public AiJobBehaviorRegistrationMode Mode { get; }
}

/// <summary>
/// Contributes list presentation independently of completion notification and result application.
/// Active presenter calls are drained before the package unloads.
/// </summary>
public abstract class AiJobPresentationExtension : Extension, ILiveUnloadExtension
{
    public abstract IReadOnlyCollection<AiJobPresentationRegistration> Registrations { get; }
}

/// <summary>
/// Contributes completion notification presentation independently of list presentation and result
/// application. Active presenter calls are drained before the package unloads.
/// </summary>
public abstract class AiJobCompletionExtension : Extension, ILiveUnloadExtension
{
    public abstract IReadOnlyCollection<AiJobCompletionRegistration> Registrations { get; }
}

/// <summary>
/// Contributes result application independently of presentation. Applicators can persist
/// package-defined scene graphs, so their packages remain loaded until restart.
/// </summary>
public abstract class AiJobResultApplicatorExtension : Extension
{
    public abstract IReadOnlyCollection<AiJobResultApplicatorRegistration> Registrations { get; }
}

public sealed record AiJobResultExtensionFailure(
    string ExtensionType,
    AiJobResultCapability Capability,
    Exception Exception);

/// <summary>Keeps a resolved list presenter alive for one operation.</summary>
public interface IAiJobPresenterLease : IDisposable
{
    IAiJobPresenter Presenter { get; }
}

/// <summary>Keeps a resolved completion presenter alive for one operation.</summary>
public interface IAiJobCompletionPresenterLease : IDisposable
{
    IAiJobCompletionPresenter Presenter { get; }
}

/// <summary>Keeps a resolved result applicator alive for one operation.</summary>
public interface IAiJobResultApplicatorLease : IDisposable
{
    IAiJobResultApplicator Applicator { get; }
}

/// <summary>
/// Resolves presentation, completion notification, and application through independent slots.
/// Replacing one capability leaves the other two unchanged.
/// </summary>
public sealed class AiJobResultRegistry : IAsyncDisposable
{
    private readonly AiJobSlotRegistry<
        IAiJobPresenter,
        AiJobPresentationRegistration,
        AiJobPresentationExtension> _presenters;
    private readonly AiJobSlotRegistry<
        IAiJobCompletionPresenter,
        AiJobCompletionRegistration,
        AiJobCompletionExtension> _completionPresenters;
    private readonly AiJobSlotRegistry<
        IAiJobResultApplicator,
        AiJobResultApplicatorRegistration,
        AiJobResultApplicatorExtension> _applicators;

    internal AiJobResultRegistry(
        IEnumerable<AiJobPresentationRegistration> presentations,
        IEnumerable<AiJobCompletionRegistration> completions,
        IEnumerable<AiJobResultApplicatorRegistration> applicators,
        IExtensionRegistry? extensionProvider,
        Action<AiJobResultExtensionFailure>? reportFailure)
    {
        _presenters = new AiJobSlotRegistry<
            IAiJobPresenter,
            AiJobPresentationRegistration,
            AiJobPresentationExtension>(
                presentations,
                static registration => registration.Kind,
                static registration => registration.Presenter,
                static registration => registration.Mode,
                static (kind, capability) => new AiJobPresentationRegistration(kind, capability),
                static extension => extension.Registrations,
                extensionProvider,
                (extension, exception) => ReportFailure(
                    reportFailure,
                    extension,
                    AiJobResultCapability.Presentation,
                    exception));
        _completionPresenters = new AiJobSlotRegistry<
            IAiJobCompletionPresenter,
            AiJobCompletionRegistration,
            AiJobCompletionExtension>(
                completions,
                static registration => registration.Kind,
                static registration => registration.Presenter,
                static registration => registration.Mode,
                static (kind, capability) => new AiJobCompletionRegistration(kind, capability),
                static extension => extension.Registrations,
                extensionProvider,
                (extension, exception) => ReportFailure(
                    reportFailure,
                    extension,
                    AiJobResultCapability.Completion,
                    exception));
        _applicators = new AiJobSlotRegistry<
            IAiJobResultApplicator,
            AiJobResultApplicatorRegistration,
            AiJobResultApplicatorExtension>(
                applicators,
                static registration => registration.Kind,
                static registration => registration.Applicator,
                static registration => registration.Mode,
                static (kind, capability) => new AiJobResultApplicatorRegistration(kind, capability),
                static extension => extension.Registrations,
                extensionProvider,
                (extension, exception) => ReportFailure(
                    reportFailure,
                    extension,
                    AiJobResultCapability.Application,
                    exception));
    }

    public bool TryAcquirePresenter(
        AiJobKindId kind,
        [NotNullWhen(true)] out IAiJobPresenterLease? lease)
    {
        if (_presenters.TryAcquire(kind, out AiJobSlotLease<IAiJobPresenter>? inner))
        {
            lease = new PresenterLease(inner);
            return true;
        }

        lease = null;
        return false;
    }

    public bool TryAcquireCompletionPresenter(
        AiJobKindId kind,
        [NotNullWhen(true)] out IAiJobCompletionPresenterLease? lease)
    {
        if (_completionPresenters.TryAcquire(
                kind,
                out AiJobSlotLease<IAiJobCompletionPresenter>? inner))
        {
            lease = new CompletionPresenterLease(inner);
            return true;
        }

        lease = null;
        return false;
    }

    public bool TryAcquireApplicator(
        AiJobKindId kind,
        [NotNullWhen(true)] out IAiJobResultApplicatorLease? lease)
    {
        if (_applicators.TryAcquire(
                kind,
                out AiJobSlotLease<IAiJobResultApplicator>? inner))
        {
            lease = new ApplicatorLease(inner);
            return true;
        }

        lease = null;
        return false;
    }

    public async ValueTask DisposeAsync()
    {
        await Task.WhenAll(
            _presenters.DisposeAsync().AsTask(),
            _completionPresenters.DisposeAsync().AsTask(),
            _applicators.DisposeAsync().AsTask());
    }

    private static void ReportFailure<TExtension>(
        Action<AiJobResultExtensionFailure>? reportFailure,
        TExtension extension,
        AiJobResultCapability capability,
        Exception exception)
        where TExtension : Extension
    {
        if (reportFailure is null)
            return;

        reportFailure(new AiJobResultExtensionFailure(
            extension.GetType().FullName ?? extension.GetType().Name,
            capability,
            exception));
    }

    private sealed class PresenterLease(
        AiJobSlotLease<IAiJobPresenter> inner) : IAiJobPresenterLease
    {
        public IAiJobPresenter Presenter => inner.Capability;

        public void Dispose() => inner.Dispose();
    }

    private sealed class CompletionPresenterLease(
        AiJobSlotLease<IAiJobCompletionPresenter> inner) : IAiJobCompletionPresenterLease
    {
        public IAiJobCompletionPresenter Presenter => inner.Capability;

        public void Dispose() => inner.Dispose();
    }

    private sealed class ApplicatorLease(
        AiJobSlotLease<IAiJobResultApplicator> inner) : IAiJobResultApplicatorLease
    {
        public IAiJobResultApplicator Applicator => inner.Capability;

        public void Dispose() => inner.Dispose();
    }
}

internal static class AiJobResultContractValidation
{
    public static void Validate(AiJobKindId kind, AiJobBehaviorRegistrationMode mode)
    {
        if (string.IsNullOrWhiteSpace(kind.Value))
            throw new ArgumentException("An AI job kind identifier is required.", nameof(kind));
        if (!Enum.IsDefined(mode))
            throw new ArgumentOutOfRangeException(nameof(mode));
    }
}
