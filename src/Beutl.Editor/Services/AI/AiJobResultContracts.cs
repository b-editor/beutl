using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using Beutl.Api.Services;
using Beutl.Extensibility;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Services.AI;

/// <summary>
/// Provides the scene-editing capabilities required to apply an AI job result without coupling
/// result handlers to a concrete desktop view model.
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
public sealed record AiJobPresentation(
    string KindDisplayName,
    string StatusDisplayName,
    string Summary,
    string Details,
    bool IsFailure);

/// <summary>
/// The notification severity for a terminal AI job.
/// </summary>
public enum AiJobNotificationKind
{
    Information,
    Success,
    Warning,
}

/// <summary>
/// Describes an editor notification for a terminal AI job.
/// </summary>
public sealed record AiJobCompletionPresentation(
    string Title,
    string Message,
    AiJobNotificationKind Notification,
    TimeSpan? Expiration = null);

/// <summary>
/// Provides editor presentation, completion notification, and result application for one AI job kind.
/// </summary>
public interface IAiJobResultHandler
{
    AiJobPresentation Present(AiJob job, AiJobStatusSemantics status);

    AiJobCompletionPresentation? CreateCompletion(
        AiJob job,
        AiJobStatusSemantics status,
        AiJobPresentation presentation);

    bool CanHandle(AiJob job, AiJobStatusSemantics status);

    Task HandleAsync(
        AiJob job,
        IAiJobResultContext context,
        CancellationToken cancellationToken);
}

/// <summary>
/// Associates one editor-side result handler with an API job kind.
/// </summary>
public sealed class AiJobResultContribution
{
    public AiJobResultContribution(AiJobKindId kind, IAiJobResultHandler handler)
    {
        if (kind.Value.Length == 0)
            throw new ArgumentException("An AI job kind identifier is required.", nameof(kind));

        Kind = kind;
        Handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    public AiJobKindId Kind { get; }

    public IAiJobResultHandler Handler { get; }
}

public enum AiJobResultHandlerRegistrationMode
{
    Add,
    Replace,
}

/// <summary>
/// Registers an editor-side result contribution with explicit replacement semantics.
/// </summary>
public sealed class AiJobResultHandlerRegistration
{
    public AiJobResultHandlerRegistration(
        AiJobResultContribution contribution,
        AiJobResultHandlerRegistrationMode mode = AiJobResultHandlerRegistrationMode.Add)
    {
        ArgumentNullException.ThrowIfNull(contribution);
        if (!Enum.IsDefined(mode))
            throw new ArgumentOutOfRangeException(nameof(mode));

        Contribution = contribution;
        Mode = mode;
    }

    public AiJobResultContribution Contribution { get; }

    public AiJobResultHandlerRegistrationMode Mode { get; }
}

/// <summary>
/// Contributes editor-side AI job result handlers independently of API job lifecycle descriptors.
/// </summary>
public abstract class AiJobResultHandlerExtension : Extension, ILiveUnloadExtension
{
    public abstract IReadOnlyCollection<AiJobResultHandlerRegistration> Registrations { get; }
}

public sealed record AiJobResultHandlerExtensionFailure(
    string ExtensionType,
    Exception Exception);

/// <summary>
/// Keeps a resolved result handler alive for one operation.
/// </summary>
public interface IAiJobResultHandlerLease : IDisposable
{
    IAiJobResultHandler Handler { get; }
}

/// <summary>
/// Owns a result-handler registration. Asynchronous disposal retires the handler synchronously,
/// then waits without blocking for calls holding an existing handler lease.
/// </summary>
public interface IAiJobResultHandlerRegistration : IAsyncDisposable
{
}

/// <summary>
/// Resolves result handlers from host and package contributions. Removing a contribution retires
/// it before package unload and waits for its active handler calls to complete.
/// </summary>
public sealed class AiJobResultHandlerRegistry : IAsyncDisposable
{
    private readonly Dictionary<AiJobKindId, List<Registration>> _registrations = [];
    private readonly Dictionary<AiJobResultHandlerExtension, List<Registration>> _extensionRegistrations =
        new(ReferenceEqualityComparer.Instance);
    private readonly object _extensionCompositionGate = new();
    private readonly object _gate = new();
    private readonly Action<AiJobResultHandlerExtensionFailure>? _reportFailure;
    private IExtensionProvider? _extensionProvider;
    private Task? _disposeTask;
    private bool _disposed;

    public AiJobResultHandlerRegistry(
        IEnumerable<AiJobResultHandlerRegistration> hostRegistrations,
        IExtensionProvider? extensionProvider = null,
        Action<AiJobResultHandlerExtensionFailure>? reportFailure = null)
    {
        ArgumentNullException.ThrowIfNull(hostRegistrations);
        _reportFailure = reportFailure;
        foreach (AiJobResultHandlerRegistration registration in hostRegistrations)
        {
            Register(registration);
        }

        if (extensionProvider is not null)
        {
            AttachExtensionProvider(extensionProvider);
        }
    }

    public IAiJobResultHandlerRegistration Register(AiJobResultHandlerRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        AiJobResultContribution contribution = registration.Contribution
            ?? throw new ArgumentException("A result-handler registration requires a contribution.", nameof(registration));
        IAiJobResultHandler handler = contribution.Handler
            ?? throw new ArgumentException("A result-handler contribution requires a handler.", nameof(registration));
        if (!Enum.IsDefined(registration.Mode))
            throw new ArgumentOutOfRangeException(nameof(registration));

        AiJobKindId kind = Normalize(contribution.Kind);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_registrations.TryGetValue(kind, out List<Registration>? registrations))
            {
                registrations = [];
                _registrations.Add(kind, registrations);
            }

            bool exists = registrations.Count > 0;
            if (registration.Mode == AiJobResultHandlerRegistrationMode.Add && exists)
            {
                throw new ArgumentException(
                    $"An AI job result handler for '{contribution.Kind}' is already registered. Use Replace explicitly.",
                    nameof(registration));
            }
            if (registration.Mode == AiJobResultHandlerRegistrationMode.Replace && !exists)
            {
                throw new ArgumentException(
                    $"An AI job result handler for '{contribution.Kind}' cannot be replaced because it is not registered.",
                    nameof(registration));
            }

            var result = new Registration(this, new RegistrationState(kind, handler));
            registrations.Add(result);
            return result;
        }
    }

    public bool TryAcquire(
        AiJobKindId kind,
        [NotNullWhen(true)] out IAiJobResultHandlerLease? lease)
    {
        if (kind.Value.Length == 0)
        {
            lease = null;
            return false;
        }

        AiJobKindId normalized = Normalize(kind);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_registrations.TryGetValue(normalized, out List<Registration>? registrations))
            {
                for (int index = registrations.Count - 1; index >= 0; index--)
                {
                    if (registrations[index].TryAcquire(out HandlerLease? handlerLease))
                    {
                        lease = handlerLease;
                        return true;
                    }

                    registrations.RemoveAt(index);
                }

                if (registrations.Count == 0)
                {
                    _registrations.Remove(normalized);
                }
            }

            lease = null;
            return false;
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_extensionProvider is IExtensionRegistry registry)
        {
            ValueTask result = default;
            registry.SynchronizeMutation(() => result = StartDispose());
            return result;
        }

        return StartDispose();
    }

    private ValueTask StartDispose()
    {
        lock (_extensionCompositionGate)
        {
            return new ValueTask(_disposeTask ??= DisposeCoreAsync());
        }
    }

    private Task DisposeCoreAsync()
    {
        Registration[] registrations;
        KeyValuePair<AiJobResultHandlerExtension, List<Registration>>[] extensionRegistrations;
        lock (_extensionCompositionGate)
        {
            lock (_gate)
            {
                if (_disposed)
                    return Task.CompletedTask;

                _disposed = true;
                registrations = _registrations.Values.SelectMany(value => value).ToArray();
                _registrations.Clear();
            }

            if (_extensionProvider is not null)
            {
                _extensionProvider.AllExtensions.CollectionChanged -= OnExtensionsChanged;
                _extensionProvider = null;
            }

            extensionRegistrations = _extensionRegistrations.ToArray();
            _extensionRegistrations.Clear();
        }

        foreach ((AiJobResultHandlerExtension extension, List<Registration> owned)
                 in extensionRegistrations)
        {
            ExtensionRegistrationLifetimes.Retire(
                extension,
                () => DisposeRegistrationsAsync(owned));
        }

        return DisposeRegistrationsAsync(registrations).AsTask();
    }

    private void AttachExtensionProvider(IExtensionProvider extensionProvider)
    {
        ArgumentNullException.ThrowIfNull(extensionProvider);
        lock (_extensionCompositionGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_extensionProvider is not null)
                throw new InvalidOperationException("An extension provider is already attached.");

            _extensionProvider = extensionProvider;
            extensionProvider.AllExtensions.CollectionChanged += OnExtensionsChanged;
            try
            {
                SynchronizeExtensionRegistrationsCore();
            }
            catch
            {
                extensionProvider.AllExtensions.CollectionChanged -= OnExtensionsChanged;
                _extensionProvider = null;
                throw;
            }
        }
    }

    private void OnExtensionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        lock (_extensionCompositionGate)
        {
            if (!_disposed)
            {
                SynchronizeExtensionRegistrationsCore();
            }
        }
    }

    private void SynchronizeExtensionRegistrationsCore()
    {
        IExtensionProvider extensionProvider = _extensionProvider
            ?? throw new InvalidOperationException("No extension provider is attached.");
        AiJobResultHandlerExtension[] currentExtensions =
            extensionProvider.GetExtensions<AiJobResultHandlerExtension>();
        var currentSet = new HashSet<AiJobResultHandlerExtension>(
            currentExtensions,
            ReferenceEqualityComparer.Instance);

        KeyValuePair<AiJobResultHandlerExtension, List<Registration>>[] removedRegistrations =
            _extensionRegistrations
            .Where(pair => !currentSet.Contains(pair.Key))
            .ToArray();
        foreach (AiJobResultHandlerExtension extension in _extensionRegistrations.Keys
                     .Where(extension => !currentSet.Contains(extension))
                     .ToArray())
        {
            _extensionRegistrations.Remove(extension);
        }

        foreach ((AiJobResultHandlerExtension extension, List<Registration> registrations)
                 in removedRegistrations)
        {
            ExtensionRegistrationLifetimes.Retire(
                extension,
                () => DisposeRegistrationsAsync(registrations));
        }

        foreach (AiJobResultHandlerExtension extension in currentExtensions)
        {
            if (_extensionRegistrations.ContainsKey(extension))
                continue;

            var registrations = new List<Registration>();
            try
            {
                foreach (AiJobResultHandlerRegistration registration in ValidateRegistrations(extension))
                {
                    registrations.Add((Registration)Register(registration));
                }

                _extensionRegistrations.Add(extension, registrations);
            }
            catch (Exception ex)
            {
                ExtensionRegistrationLifetimes.Retire(
                    extension,
                    () => DisposeRegistrationsAsync(registrations));

                ReportFailure(extension, ex);
            }
        }
    }

    private Task UnregisterAsync(Registration registration, RegistrationState state)
    {
        Task drain;
        lock (_gate)
        {
            drain = state.RetireAsync();
            if (_registrations.TryGetValue(state.Kind, out List<Registration>? registrations))
            {
                registrations.Remove(registration);
                if (registrations.Count == 0)
                {
                    _registrations.Remove(state.Kind);
                }
            }
        }

        return drain;
    }

    private static ValueTask DisposeRegistrationsAsync(IEnumerable<Registration> registrations)
    {
        Task[] drains = registrations
            .Select(registration => registration.DisposeAsync().AsTask())
            .ToArray();
        return drains.Length == 0
            ? ValueTask.CompletedTask
            : new ValueTask(Task.WhenAll(drains));
    }

    private static AiJobResultHandlerRegistration[] ValidateRegistrations(
        AiJobResultHandlerExtension extension)
    {
        IReadOnlyCollection<AiJobResultHandlerRegistration>? registrations = extension.Registrations;
        if (registrations is null)
        {
            throw new InvalidOperationException(
                "An AI job result-handler extension returned a null registration collection.");
        }

        AiJobResultHandlerRegistration[] snapshot = registrations.ToArray();
        if (snapshot.Any(registration => registration is null))
        {
            throw new InvalidOperationException(
                "An AI job result-handler extension returned a null registration.");
        }

        return snapshot;
    }

    private void ReportFailure(AiJobResultHandlerExtension extension, Exception exception)
    {
        if (_reportFailure is null)
            return;

        try
        {
            _reportFailure(new AiJobResultHandlerExtensionFailure(
                extension.GetType().FullName ?? extension.GetType().Name,
                exception));
        }
        catch
        {
            // Diagnostics must not interrupt extension removal before Extension.Unload().
        }
    }

    private static AiJobKindId Normalize(AiJobKindId kind)
        => new(kind.Value.ToLowerInvariant());

    private sealed class RegistrationState(AiJobKindId kind, IAiJobResultHandler handler)
    {
        private readonly object _gate = new();
        private TaskCompletionSource? _drained;
        private int _activeLeases;
        private bool _retired;

        public AiJobKindId Kind { get; } = kind;

        public IAiJobResultHandler Handler { get; } = handler;

        public bool TryAcquire([NotNullWhen(true)] out HandlerLease? lease)
        {
            lock (_gate)
            {
                if (_retired)
                {
                    lease = null;
                    return false;
                }

                _activeLeases++;
                lease = new HandlerLease(this);
                return true;
            }
        }

        public Task RetireAsync()
        {
            lock (_gate)
            {
                _retired = true;
                return _activeLeases == 0
                    ? Task.CompletedTask
                    : (_drained ??= new TaskCompletionSource(
                        TaskCreationOptions.RunContinuationsAsynchronously)).Task;
            }
        }

        public void ReleaseLease()
        {
            TaskCompletionSource? drained = null;
            lock (_gate)
            {
                _activeLeases--;
                if (_activeLeases == 0 && _retired)
                {
                    drained = _drained;
                }
            }

            drained?.TrySetResult();
        }
    }

    private sealed class Registration : IAiJobResultHandlerRegistration
    {
        private readonly Lazy<Task> _retirement;
        private RegistrationOwner? _owner;

        public Registration(AiJobResultHandlerRegistry registry, RegistrationState state)
        {
            _owner = new RegistrationOwner(registry, state);
            _retirement = new Lazy<Task>(
                RetireCoreAsync,
                LazyThreadSafetyMode.ExecutionAndPublication);
        }

        public bool TryAcquire([NotNullWhen(true)] out HandlerLease? lease)
        {
            RegistrationOwner? owner = Volatile.Read(ref _owner);
            if (_retirement.IsValueCreated || owner is null)
            {
                lease = null;
                return false;
            }

            return owner.State.TryAcquire(out lease);
        }

        public ValueTask DisposeAsync()
            => new(_retirement.Value);

        private async Task RetireCoreAsync()
        {
            RegistrationOwner? owner = Volatile.Read(ref _owner);
            if (owner is null)
                return;

            try
            {
                await owner.Registry.UnregisterAsync(this, owner.State).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Exchange(ref _owner, null);
            }
        }

        private sealed record RegistrationOwner(
            AiJobResultHandlerRegistry Registry,
            RegistrationState State);
    }

    private sealed class HandlerLease(RegistrationState state) : IAiJobResultHandlerLease
    {
        private RegistrationState? _state = state;

        public IAiJobResultHandler Handler
            => Volatile.Read(ref _state)?.Handler
                ?? throw new ObjectDisposedException(nameof(IAiJobResultHandlerLease));

        public void Dispose()
        {
            RegistrationState? state = Interlocked.Exchange(ref _state, null);
            state?.ReleaseLease();
        }
    }
}
