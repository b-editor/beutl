using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using Beutl.Api.Services;
using Beutl.Collections;
using Beutl.Extensibility;

namespace Beutl.Editor.Services;

/// <summary>
/// Resolves source handlers from host and package contributions. Removing a contribution retires
/// it before package unload and waits for active materialization calls to finish.
/// </summary>
public sealed partial class ElementSourceHandlerRegistry : IElementSourceHandlerRegistry, IAsyncDisposable
{
    private readonly Dictionary<Type, List<Registration>> _handlers = [];
    private readonly CoreList<ElementSourceHandlerDescriptor> _handlerMetadata = [];
    private readonly HashSet<Task> _activeRegistrationRetirements = [];
    private readonly Dictionary<ElementSourceHandlerExtension, List<Registration>> _extensionRegistrations =
        new(ReferenceEqualityComparer.Instance);
    private readonly object _extensionCompositionGate = new();
    private readonly object _gate = new();
    private readonly Action<ElementSourceHandlerExtensionFailure>? _reportFailure;
    private IExtensionRegistry? _extensionProvider;
    private Task? _disposeTask;
    private long _registrationSequence;
    private int _metadataBatchDepth;
    private bool _disposed;

    public ElementSourceHandlerRegistry()
        : this([])
    {
    }

    public ElementSourceHandlerRegistry(
        IEnumerable<ElementSourceHandlerRegistration> hostRegistrations)
        : this(hostRegistrations, null, null)
    {
    }

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

    public ICoreReadOnlyList<ElementSourceHandlerDescriptor> Handlers => _handlerMetadata;

    public IElementSourceHandlerRegistration Register(ElementSourceHandlerRegistration registration)
    {
        PreparedRegistration prepared = PrepareRegistration(registration);

        lock (_extensionCompositionGate)
        {
            Registration result;
            ElementSourceHandlerDescriptor[] metadata;
            lock (_gate)
            {
                result = RegisterPrepared_NoLock(prepared);
                metadata = CreateMetadata_NoLock();
            }
            PublishMetadata(metadata);
            return result;
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
        ElementSourceHandlerDescriptor[] metadata;
        KeyValuePair<ElementSourceHandlerExtension, List<Registration>>[] extensionRegistrations;
        lock (_extensionCompositionGate)
        {
            lock (_gate)
            {
                if (_disposed)
                    return Task.CompletedTask;

                _disposed = true;
                registrations = _handlers.Values.SelectMany(value => value).ToArray();
                _handlers.Clear();
                metadata = CreateMetadata_NoLock();
                activeRetirements = _activeRegistrationRetirements.ToArray();
                _activeRegistrationRetirements.Clear();
            }

            PublishMetadata(metadata);

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
        ArgumentNullException.ThrowIfNull(extensionProvider);
        extensionProvider.SynchronizeMutation(() =>
        {
            lock (_extensionCompositionGate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_extensionProvider is not null)
                    throw new InvalidOperationException("An extension provider is already attached.");

                _extensionProvider = extensionProvider;
                extensionProvider.AllExtensions.CollectionChanged += OnExtensionsChanged;
                extensionProvider.ExtensionsChanged += OnExtensionsCommitted;
                try
                {
                    SynchronizeExtensionRegistrationsCore();
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
                SynchronizeExtensionRegistrationsCore();
            }
        }
    }

    private void OnExtensionsCommitted(object? sender, EventArgs e)
    {
        lock (_extensionCompositionGate)
        {
            if (!_disposed)
                SynchronizeExtensionRegistrationsCore();
        }
    }

    private void SynchronizeExtensionRegistrationsCore()
    {
        IExtensionRegistry extensionProvider = _extensionProvider
            ?? throw new InvalidOperationException("No extension provider is attached.");
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
        ElementSourceHandlerDescriptor[] metadata;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _metadataBatchDepth++;
            try
            {
                RetireRemovedRegistrations_NoLock(removedRegistrations);
                RegisterCandidates_NoLock(candidates, failures);
            }
            finally
            {
                _metadataBatchDepth--;
            }

            metadata = CreateMetadata_NoLock();
        }

        PublishMetadata(metadata);

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

    private void RegisterCandidates_NoLock(
        List<(ElementSourceHandlerExtension Extension, PreparedRegistration[] Registrations)> candidates,
        Dictionary<ElementSourceHandlerExtension, Exception> failures)
    {
        while (true)
        {
            var attemptOwned = new Dictionary<
                ElementSourceHandlerExtension,
                List<Registration>>(ReferenceEqualityComparer.Instance);
            foreach ((ElementSourceHandlerExtension extension, _) in candidates)
            {
                if (!failures.ContainsKey(extension))
                    attemptOwned.Add(extension, []);
            }
            Dictionary<ElementSourceHandlerExtension, (Exception Exception, int Phase)> newFailures =
                RegisterAttempt_NoLock(candidates, failures, attemptOwned);

            if (newFailures.Count > 0)
            {
                foreach (List<Registration> owned in attemptOwned.Values)
                {
                    DisposeRegistrationsAsync(owned).AsTask().GetAwaiter().GetResult();
                }
                int latestFailedPhase = newFailures.Values.Max(failure => failure.Phase);
                KeyValuePair<ElementSourceHandlerExtension, (Exception Exception, int Phase)> rejected =
                    newFailures.First(failure => failure.Value.Phase == latestFailedPhase);
                failures.TryAdd(rejected.Key, rejected.Value.Exception);
                continue;
            }

            foreach ((ElementSourceHandlerExtension extension, List<Registration> owned)
                     in attemptOwned)
            {
                _extensionRegistrations.Add(extension, owned);
            }
            break;
        }
    }

    private Dictionary<ElementSourceHandlerExtension, (Exception Exception, int Phase)> RegisterAttempt_NoLock(
        List<(ElementSourceHandlerExtension Extension, PreparedRegistration[] Registrations)> candidates,
        Dictionary<ElementSourceHandlerExtension, Exception> failures,
        Dictionary<ElementSourceHandlerExtension, List<Registration>> attemptOwned)
    {
        var newFailures = new Dictionary<
            ElementSourceHandlerExtension,
            (Exception Exception, int Phase)>(ReferenceEqualityComparer.Instance);
        ElementSourceHandlerRegistrationMode[] phases =
        [
            ElementSourceHandlerRegistrationMode.Add,
            ElementSourceHandlerRegistrationMode.Replace,
        ];
        for (int phaseIndex = 0; phaseIndex < phases.Length; phaseIndex++)
        {
            ElementSourceHandlerRegistrationMode mode = phases[phaseIndex];
            foreach ((ElementSourceHandlerExtension extension,
                     PreparedRegistration[] registrations) in candidates)
            {
                if (failures.ContainsKey(extension) || newFailures.ContainsKey(extension))
                    continue;
                try
                {
                    foreach (PreparedRegistration registration in registrations
                                 .Where(registration => registration.Mode == mode))
                    {
                        attemptOwned[extension].Add(RegisterPrepared_NoLock(registration));
                    }
                }
                catch (Exception ex)
                {
                    newFailures.Add(extension, (ex, phaseIndex));
                }
            }
        }

        return newFailures;
    }

    private Task UnregisterAsync(Registration registration, RegistrationState state)
    {
        Task drain;
        ElementSourceHandlerDescriptor[]? metadata = null;
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

                if (_metadataBatchDepth == 0)
                    metadata = CreateMetadata_NoLock();
            }

            if (metadata is not null)
                PublishMetadata(metadata);
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

    private ElementSourceHandlerDescriptor[] CreateMetadata_NoLock()
        => _handlers
            .Select(pair => pair.Value[^1].State)
            .OrderBy(state => state.Order)
            .ThenBy(state => state.SourceType.FullName, StringComparer.Ordinal)
            .ThenBy(state => state.Sequence)
            .Select(state => state.Descriptor)
            .ToArray();

    private void PublishMetadata(ElementSourceHandlerDescriptor[] metadata)
    {
        if (_handlerMetadata.Count == metadata.Length
            && _handlerMetadata.Select((current, index) =>
                    current.SourceTypeName == metadata[index].SourceTypeName
                    && current.Order == metadata[index].Order)
                .All(matches => matches))
        {
            return;
        }

        try
        {
            _handlerMetadata.Replace(metadata);
        }
        catch
        {
            // Metadata observers cannot veto or roll back handler registration changes.
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
        if (!typeof(Models.ElementSource).IsAssignableFrom(sourceType))
        {
            throw new ArgumentException(
                $"Source type '{sourceType.FullName}' does not derive from ElementSource.",
                nameof(handler));
        }

        return new PreparedRegistration(
            sourceType,
            handler,
            registration.Mode,
            registration.Order);
    }

    private Registration RegisterPrepared_NoLock(PreparedRegistration registration)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        bool exists = _handlers.TryGetValue(registration.SourceType, out List<Registration>? entries)
            && entries.Count > 0;
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

        entries ??= [];
        _handlers[registration.SourceType] = entries;
        var state = new RegistrationState(
            registration.SourceType,
            registration.Handler,
            registration.Order,
            ++_registrationSequence);
        var owner = new Registration(this, state);
        entries.Add(owner);
        return owner;
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
        ElementSourceHandlerRegistrationMode Mode,
        int Order);

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
