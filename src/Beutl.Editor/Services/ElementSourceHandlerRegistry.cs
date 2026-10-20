using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using Beutl.Api.Services;
using Beutl.Extensibility;

namespace Beutl.Editor.Services;

/// <summary>
/// Resolves source handlers from host and package contributions. Removing a contribution retires
/// it before package unload and waits for active materialization calls to finish.
/// </summary>
public sealed partial class ElementSourceHandlerRegistry : IElementSourceHandlerRegistry, IAsyncDisposable
{
    // The two composition phases: every Add registration, then every Replace one.
    private static readonly ElementSourceHandlerRegistrationMode[] s_phases =
    [
        ElementSourceHandlerRegistrationMode.Add,
        ElementSourceHandlerRegistrationMode.Replace,
    ];

    private readonly Dictionary<Type, List<Registration>> _handlers = [];
    private readonly HashSet<Task> _activeRegistrationRetirements = [];
    private readonly Dictionary<ElementSourceHandlerExtension, List<Registration>> _extensionRegistrations =
        new(ReferenceEqualityComparer.Instance);
    private readonly object _extensionCompositionGate = new();
    private readonly object _gate = new();
    private readonly Action<ElementSourceHandlerExtensionFailure>? _reportFailure;
    private IExtensionRegistry? _extensionProvider;
    private Task? _disposeTask;
    private bool _disposed;

    internal ElementSourceHandlerRegistry(
        IEnumerable<ElementSourceHandlerRegistration> hostRegistrations,
        IExtensionRegistry? extensionProvider,
        Action<ElementSourceHandlerExtensionFailure>? reportFailure = null)
    {
        ArgumentNullException.ThrowIfNull(hostRegistrations);
        _reportFailure = reportFailure;
        foreach (ElementSourceHandlerRegistration registration in hostRegistrations)
        {
            Register(registration);
        }

        if (extensionProvider is not null)
        {
            AttachExtensionProvider(extensionProvider);
        }
    }

    private void Register(ElementSourceHandlerRegistration registration)
    {
        PreparedRegistration prepared = PrepareRegistration(registration);

        lock (_extensionCompositionGate)
        {
            lock (_gate)
            {
                RegisterPrepared_NoLock(prepared);
            }
        }
    }

    public bool TryAcquire(
        Type sourceType,
        [NotNullWhen(true)] out IElementSourceHandlerLease? lease)
    {
        ArgumentNullException.ThrowIfNull(sourceType);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_handlers.TryGetValue(sourceType, out List<Registration>? entries))
            {
                for (int index = entries.Count - 1; index >= 0; index--)
                {
                    Registration registration = entries[index];
                    if (registration.TryAcquire(out HandlerLease? handlerLease))
                    {
                        lease = handlerLease;
                        return true;
                    }

                    entries.RemoveAt(index);
                }

                _handlers.Remove(sourceType);
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
            if (_disposeTask is not null)
                return new ValueTask(_disposeTask);

            var completion = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _disposeTask = completion.Task;
            try
            {
                _ = CompleteDisposeAsync(DisposeCoreAsync(), completion);
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }

            return new ValueTask(_disposeTask);
        }
    }

    private static async Task CompleteDisposeAsync(
        Task dispose,
        TaskCompletionSource completion)
    {
        try
        {
            await dispose.ConfigureAwait(false);
            completion.TrySetResult();
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
        }
    }

    private Task DisposeCoreAsync()
    {
        Registration[] registrations;
        Task[] activeRetirements;
        KeyValuePair<ElementSourceHandlerExtension, List<Registration>>[] extensionRegistrations;
        lock (_extensionCompositionGate)
        {
            lock (_gate)
            {
                _disposed = true;
                registrations = _handlers.Values.SelectMany(value => value).ToArray();
                _handlers.Clear();
                activeRetirements = _activeRegistrationRetirements.ToArray();
                _activeRegistrationRetirements.Clear();
            }

            if (_extensionProvider is not null)
            {
                _extensionProvider.AllExtensions.CollectionChanged -= OnExtensionsChanged;
                _extensionProvider.ExtensionsChanged -= OnExtensionsCommitted;
                _extensionProvider = null;
            }

            extensionRegistrations = _extensionRegistrations.ToArray();
            _extensionRegistrations.Clear();
        }

        foreach ((ElementSourceHandlerExtension extension, List<Registration> owned)
                 in extensionRegistrations)
        {
            ExtensionRegistrationLifetimes.Retire(
                extension,
                () => DisposeRegistrationsAsync(owned));
        }

        Task registrationsDisposed = DisposeRegistrationsAsync(registrations).AsTask();
        return Task.WhenAll(activeRetirements.Append(registrationsDisposed));
    }

    private void AttachExtensionProvider(IExtensionRegistry extensionProvider)
    {
        extensionProvider.SynchronizeMutation(() =>
        {
            lock (_extensionCompositionGate)
            {
                _extensionProvider = extensionProvider;
                extensionProvider.AllExtensions.CollectionChanged += OnExtensionsChanged;
                extensionProvider.ExtensionsChanged += OnExtensionsCommitted;
                try
                {
                    SynchronizeExtensionRegistrationsCore(extensionProvider);
                }
                catch
                {
                    extensionProvider.AllExtensions.CollectionChanged -= OnExtensionsChanged;
                    extensionProvider.ExtensionsChanged -= OnExtensionsCommitted;
                    _extensionProvider = null;
                    throw;
                }
            }
        });
    }

    private void OnExtensionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
            return;

        lock (_extensionCompositionGate)
        {
            if (!_disposed)
            {
                SynchronizeExtensionRegistrationsCore(_extensionProvider!);
            }
        }
    }

    private void OnExtensionsCommitted(object? sender, EventArgs e)
    {
        lock (_extensionCompositionGate)
        {
            if (!_disposed)
                SynchronizeExtensionRegistrationsCore(_extensionProvider!);
        }
    }

    private void SynchronizeExtensionRegistrationsCore(IExtensionRegistry extensionProvider)
    {
        ElementSourceHandlerExtension[] currentExtensions =
            extensionProvider.GetExtensions<ElementSourceHandlerExtension>();
        var currentSet = new HashSet<ElementSourceHandlerExtension>(
            currentExtensions,
            ReferenceEqualityComparer.Instance);

        KeyValuePair<ElementSourceHandlerExtension, List<Registration>>[] removedRegistrations =
            _extensionRegistrations
            .Where(pair => !currentSet.Contains(pair.Key))
            .ToArray();

        List<(ElementSourceHandlerExtension Extension, PreparedRegistration[] Registrations)> candidates =
            CreateCandidates(currentExtensions);

        var failures = new Dictionary<ElementSourceHandlerExtension, Exception>(
            ReferenceEqualityComparer.Instance);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            RetireRemovedRegistrations_NoLock(removedRegistrations);
            RegisterCandidates_NoLock(candidates, failures);
        }

        foreach ((ElementSourceHandlerExtension extension, Exception failure) in failures)
            ReportFailure(extension, failure);
    }

    private List<(ElementSourceHandlerExtension Extension, PreparedRegistration[] Registrations)> CreateCandidates(
        ElementSourceHandlerExtension[] currentExtensions)
    {
        var candidates = new List<(
            ElementSourceHandlerExtension Extension,
            PreparedRegistration[] Registrations)>();
        foreach (ElementSourceHandlerExtension extension in currentExtensions)
        {
            if (_extensionRegistrations.ContainsKey(extension))
                continue;

            try
            {
                candidates.Add((
                    extension,
                    ValidateRegistrations(extension).Select(PrepareRegistration).ToArray()));
            }
            catch (Exception ex)
            {
                ReportFailure(extension, ex);
            }
        }

        return candidates;
    }

    private void RetireRemovedRegistrations_NoLock(
        KeyValuePair<ElementSourceHandlerExtension, List<Registration>>[] removedRegistrations)
    {
        foreach ((ElementSourceHandlerExtension extension, List<Registration> registrations)
                 in removedRegistrations)
        {
            _extensionRegistrations.Remove(extension);
            ExtensionRegistrationLifetimes.Retire(
                extension,
                () => DisposeRegistrationsAsync(registrations));
        }
    }

    // Composes the candidates against the registered source types first, so that only the accepted
    // registrations are added to the live state.
    private void RegisterCandidates_NoLock(
        List<(ElementSourceHandlerExtension Extension, PreparedRegistration[] Registrations)> candidates,
        Dictionary<ElementSourceHandlerExtension, Exception> failures)
    {
        Type[] registeredTypes = _handlers
            .Where(pair => pair.Value.Count > 0)
            .Select(pair => pair.Key)
            .ToArray();
        _ = TwoPhaseComposition.Compose(
            candidates,
            static candidate => candidate.Extension,
            failures,
            () => new HashSet<Type>(registeredTypes),
            (registered, candidate, phaseIndex) =>
            {
                foreach (PreparedRegistration registration in candidate.Registrations
                             .Where(registration => registration.Mode == s_phases[phaseIndex]))
                {
                    ValidateRegistrationMode(registration, registered.Contains(registration.SourceType));
                    registered.Add(registration.SourceType);
                }

                return registered;
            });

        var owned = new Dictionary<ElementSourceHandlerExtension, List<Registration>>(
            ReferenceEqualityComparer.Instance);
        foreach ((ElementSourceHandlerExtension extension, _) in candidates)
        {
            if (!failures.ContainsKey(extension))
                owned.Add(extension, []);
        }

        foreach (ElementSourceHandlerRegistrationMode mode in s_phases)
        {
            foreach ((ElementSourceHandlerExtension extension,
                     PreparedRegistration[] registrations) in candidates)
            {
                if (!owned.TryGetValue(extension, out List<Registration>? registered))
                    continue;

                foreach (PreparedRegistration registration in registrations
                             .Where(registration => registration.Mode == mode))
                {
                    registered.Add(RegisterPrepared_NoLock(registration));
                }
            }
        }

        foreach ((ElementSourceHandlerExtension extension, List<Registration> registered) in owned)
        {
            _extensionRegistrations.Add(extension, registered);
        }
    }

    private Task UnregisterAsync(Registration registration, RegistrationState state)
    {
        Task drain;
        lock (_extensionCompositionGate)
        {
            lock (_gate)
            {
                drain = state.RetireAsync();
                if (!drain.IsCompleted)
                {
                    _activeRegistrationRetirements.Add(drain);
                    _ = ForgetRetirementWhenCompleteAsync(drain);
                }

                if (_handlers.TryGetValue(state.SourceType, out List<Registration>? entries))
                {
                    entries.Remove(registration);
                    if (entries.Count == 0)
                    {
                        _handlers.Remove(state.SourceType);
                    }
                }
            }
        }

        return drain;
    }

    private async Task ForgetRetirementWhenCompleteAsync(Task retirement)
    {
        try
        {
            await retirement.ConfigureAwait(false);
        }
        catch
        {
            // The registration owner and any concurrent registry disposal still observe the
            // original task. This continuation only releases completed tracking state.
        }
        finally
        {
            lock (_gate)
            {
                _activeRegistrationRetirements.Remove(retirement);
            }
        }
    }

    private static PreparedRegistration PrepareRegistration(
        ElementSourceHandlerRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        IElementSourceHandler handler = registration.Handler;
        ArgumentNullException.ThrowIfNull(handler);
        Type sourceType = handler.SourceType
            ?? throw new ArgumentException("A source handler must declare its source type.", nameof(handler));
        if (!typeof(ElementSource).IsAssignableFrom(sourceType))
        {
            throw new ArgumentException(
                $"Source type '{sourceType.FullName}' does not derive from ElementSource.",
                nameof(handler));
        }

        return new PreparedRegistration(
            sourceType,
            handler,
            registration.Mode);
    }

    private Registration RegisterPrepared_NoLock(PreparedRegistration registration)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        bool exists = _handlers.TryGetValue(registration.SourceType, out List<Registration>? entries)
            && entries.Count > 0;
        ValidateRegistrationMode(registration, exists);

        entries ??= [];
        _handlers[registration.SourceType] = entries;
        var state = new RegistrationState(
            registration.SourceType,
            registration.Handler);
        var owner = new Registration(this, state);
        entries.Add(owner);
        return owner;
    }

    private static void ValidateRegistrationMode(PreparedRegistration registration, bool exists)
    {
        if (registration.Mode == ElementSourceHandlerRegistrationMode.Add && exists)
        {
            throw new ArgumentException(
                $"A handler for element source '{registration.SourceType.FullName}' is already registered. "
                + "Use Replace explicitly.",
                nameof(registration));
        }
        if (registration.Mode == ElementSourceHandlerRegistrationMode.Replace && !exists)
        {
            throw new ArgumentException(
                $"A handler for element source '{registration.SourceType.FullName}' cannot be replaced "
                + "because it is not registered.",
                nameof(registration));
        }
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

    private static ElementSourceHandlerRegistration[] ValidateRegistrations(
        ElementSourceHandlerExtension extension)
    {
        IReadOnlyCollection<ElementSourceHandlerRegistration>? registrations = extension.Registrations;
        if (registrations is null)
        {
            throw new InvalidOperationException(
                "An element source-handler extension returned a null registration collection.");
        }

        ElementSourceHandlerRegistration[] snapshot = registrations.ToArray();
        if (snapshot.Any(registration => registration is null))
        {
            throw new InvalidOperationException(
                "An element source-handler extension returned a null registration.");
        }

        return snapshot;
    }

    private readonly record struct PreparedRegistration(
        Type SourceType,
        IElementSourceHandler Handler,
        ElementSourceHandlerRegistrationMode Mode);

    private void ReportFailure(ElementSourceHandlerExtension extension, Exception exception)
    {
        if (_reportFailure is null)
            return;

        try
        {
            _reportFailure(new ElementSourceHandlerExtensionFailure(
                extension.GetType().FullName ?? extension.GetType().Name,
                exception));
        }
        catch
        {
            // Diagnostics must not interrupt extension removal before Extension.Unload().
        }
    }
}
