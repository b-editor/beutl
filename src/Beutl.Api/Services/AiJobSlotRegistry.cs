using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using Beutl.Extensibility;

namespace Beutl.Api.Services;

internal enum AiJobSlotRegistrationMode
{
    Add,
    Replace,
}

internal interface IAiJobSlotRegistration : IAsyncDisposable
{
}

internal sealed class AiJobSlotRegistry<TCapability, TRegistration, TExtension>
    : IAsyncDisposable
    where TCapability : class
    where TRegistration : class
    where TExtension : Extension
{
    private readonly Dictionary<AiJobKindId, List<Registration>> _registrations = [];
    private readonly HashSet<Task> _activeRegistrationRetirements = [];
    private readonly Dictionary<TExtension, List<Registration>> _extensionRegistrations =
        new(ReferenceEqualityComparer.Instance);
    private readonly object _extensionCompositionGate = new();
    private readonly object _gate = new();
    private readonly Func<TRegistration, AiJobKindId> _getKind;
    private readonly Func<TRegistration, TCapability> _getCapability;
    private readonly Func<TRegistration, AiJobSlotRegistrationMode> _getMode;
    private readonly Func<AiJobKindId, TCapability, TRegistration> _createRegistration;
    private readonly Func<TExtension, IReadOnlyCollection<TRegistration>?> _getExtensionRegistrations;
    private readonly Action<TExtension, Exception>? _reportFailure;
    private IExtensionRegistry? _extensionProvider;
    private Task? _disposeTask;
    private bool _disposed;

    public AiJobSlotRegistry(
        IEnumerable<TRegistration> hostRegistrations,
        Func<TRegistration, AiJobKindId> getKind,
        Func<TRegistration, TCapability> getCapability,
        Func<TRegistration, AiJobSlotRegistrationMode> getMode,
        Func<AiJobKindId, TCapability, TRegistration> createRegistration,
        Func<TExtension, IReadOnlyCollection<TRegistration>?> getExtensionRegistrations,
        IExtensionRegistry? extensionProvider,
        Action<TExtension, Exception>? reportFailure)
    {
        ArgumentNullException.ThrowIfNull(hostRegistrations);
        _getKind = getKind ?? throw new ArgumentNullException(nameof(getKind));
        _getCapability = getCapability ?? throw new ArgumentNullException(nameof(getCapability));
        _getMode = getMode ?? throw new ArgumentNullException(nameof(getMode));
        _createRegistration = createRegistration ?? throw new ArgumentNullException(nameof(createRegistration));
        _getExtensionRegistrations = getExtensionRegistrations
            ?? throw new ArgumentNullException(nameof(getExtensionRegistrations));
        _reportFailure = reportFailure;

        foreach (TRegistration registration in hostRegistrations)
        {
            Register(registration);
        }

        if (extensionProvider is not null)
        {
            AttachExtensionProvider(extensionProvider);
        }
    }

    public IAiJobSlotRegistration Register(TRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        AiJobKindId kind = _getKind(registration);
        TCapability capability = _getCapability(registration)
            ?? throw new ArgumentException("A capability registration requires a capability.", nameof(registration));
        AiJobSlotRegistrationMode mode = _getMode(registration);
        Validate(kind, mode);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return RegisterCore_NoLock(registration, Normalize(kind), capability, mode);
        }
    }

    public bool TryAcquire(
        AiJobKindId kind,
        [NotNullWhen(true)] out AiJobSlotLease<TCapability>? lease)
    {
        if (string.IsNullOrWhiteSpace(kind.Value))
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
                    if (registrations[index].TryAcquire(out AiJobSlotLease<TCapability>? result))
                    {
                        lease = result;
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

    private Registration RegisterCore_NoLock(
        TRegistration registration,
        AiJobKindId kind,
        TCapability capability,
        AiJobSlotRegistrationMode mode)
    {
        if (!_registrations.TryGetValue(kind, out List<Registration>? registrations))
        {
            registrations = [];
            _registrations.Add(kind, registrations);
        }

        bool exists = registrations.Count > 0;
        if (mode == AiJobSlotRegistrationMode.Add && exists)
        {
            throw new ArgumentException(
                $"An AI job capability for '{_getKind(registration)}' is already registered. Use Replace explicitly.",
                nameof(registration));
        }
        if (mode == AiJobSlotRegistrationMode.Replace && !exists)
        {
            throw new ArgumentException(
                $"An AI job capability for '{_getKind(registration)}' cannot be replaced because it is not registered.",
                nameof(registration));
        }

        var result = new Registration(this, new RegistrationState(kind, capability));
        registrations.Add(result);
        return result;
    }

    private Registration RegisterCore_NoLock(TRegistration registration)
        => RegisterCore_NoLock(
            registration,
            Normalize(_getKind(registration)),
            _getCapability(registration),
            _getMode(registration));

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
        Task[] activeRetirements;
        KeyValuePair<TExtension, List<Registration>>[] extensionRegistrations;
        lock (_extensionCompositionGate)
        {
            lock (_gate)
            {
                if (_disposed)
                    return Task.CompletedTask;

                _disposed = true;
                registrations = _registrations.Values.SelectMany(value => value).ToArray();
                _registrations.Clear();
                activeRetirements = _activeRegistrationRetirements.ToArray();
                _activeRegistrationRetirements.Clear();
            }

            if (_extensionProvider is not null)
            {
                _extensionProvider.AllExtensions.CollectionChanged -= OnExtensionCollectionChanged;
                _extensionProvider.ExtensionsChanged -= OnCommittedExtensionsChanged;
                _extensionProvider = null;
            }

            extensionRegistrations = _extensionRegistrations.ToArray();
            _extensionRegistrations.Clear();
        }

        foreach ((TExtension extension, List<Registration> owned) in extensionRegistrations)
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
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_extensionProvider is not null)
                    throw new InvalidOperationException("An extension provider is already attached.");

                _extensionProvider = extensionProvider;
                extensionProvider.AllExtensions.CollectionChanged += OnExtensionCollectionChanged;
                extensionProvider.ExtensionsChanged += OnCommittedExtensionsChanged;
                try
                {
                    SynchronizeExtensionRegistrationsCore();
                }
                catch
                {
                    extensionProvider.AllExtensions.CollectionChanged -= OnExtensionCollectionChanged;
                    extensionProvider.ExtensionsChanged -= OnCommittedExtensionsChanged;
                    _extensionProvider = null;
                    throw;
                }
            }
        });
    }

    private void OnExtensionCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
            return;

        SynchronizeExtensionRegistrations();
    }

    private void OnCommittedExtensionsChanged(object? sender, EventArgs e)
        => SynchronizeExtensionRegistrations();

    private void SynchronizeExtensionRegistrations()
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
        IExtensionRegistry extensionProvider = _extensionProvider
            ?? throw new InvalidOperationException("No extension provider is attached.");
        TExtension[] currentExtensions = extensionProvider.GetExtensions<TExtension>();
        var currentSet = new HashSet<TExtension>(
            currentExtensions,
            ReferenceEqualityComparer.Instance);

        KeyValuePair<TExtension, List<Registration>>[] removedRegistrations =
            _extensionRegistrations
                .Where(pair => !currentSet.Contains(pair.Key))
                .ToArray();

        var candidates = new List<(TExtension Extension, TRegistration[] Registrations)>();
        foreach (TExtension extension in currentExtensions)
        {
            if (_extensionRegistrations.ContainsKey(extension))
                continue;

            try
            {
                candidates.Add((extension, ValidateRegistrations(extension)));
            }
            catch (Exception ex)
            {
                ReportFailure(extension, ex);
            }
        }

        var failures = new Dictionary<TExtension, Exception>(ReferenceEqualityComparer.Instance);
        List<(TExtension Extension, List<Registration> Owned)> retired = [];
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var removedRegistrationSet = new HashSet<Registration>(
                removedRegistrations.SelectMany(pair => pair.Value),
                ReferenceEqualityComparer.Instance);
            TRegistration[] baseRegistrations = _registrations
                .Select(pair => new
                {
                    pair.Key,
                    Registration = pair.Value.LastOrDefault(
                        registration => !removedRegistrationSet.Contains(registration)),
                })
                .Where(pair => pair.Registration is not null)
                .OrderBy(pair => pair.Key.Value, StringComparer.Ordinal)
                .Select(pair => pair.Registration!.ToSeedRegistration())
                .ToArray();

            while (true)
            {
                var newFailures = new Dictionary<TExtension, (Exception Exception, int Phase)>(
                    ReferenceEqualityComparer.Instance);
                var working = new List<TRegistration>(baseRegistrations);
                AiJobSlotRegistrationMode[] phases =
                [
                    AiJobSlotRegistrationMode.Add,
                    AiJobSlotRegistrationMode.Replace,
                ];

                for (int phaseIndex = 0; phaseIndex < phases.Length; phaseIndex++)
                {
                    AiJobSlotRegistrationMode mode = phases[phaseIndex];
                    foreach ((TExtension extension, TRegistration[] registrations) in candidates)
                    {
                        if (failures.ContainsKey(extension) || newFailures.ContainsKey(extension))
                            continue;

                        TRegistration[] phase = registrations
                            .Where(registration => _getMode(registration) == mode)
                            .ToArray();
                        if (phase.Length == 0)
                            continue;

                        try
                        {
                            _ = CreateScratchRegistry(working.Concat(phase));
                            working.AddRange(phase);
                        }
                        catch (Exception ex)
                        {
                            newFailures.Add(extension, (ex, phaseIndex));
                        }
                    }
                }

                if (newFailures.Count > 0)
                {
                    int latestFailedPhase = newFailures.Values.Max(failure => failure.Phase);
                    KeyValuePair<TExtension, (Exception Exception, int Phase)> rejected =
                        newFailures.First(failure => failure.Value.Phase == latestFailedPhase);
                    failures.TryAdd(rejected.Key, rejected.Value.Exception);
                    continue;
                }

                foreach ((TExtension extension, List<Registration> registrations)
                         in removedRegistrations)
                {
                    _extensionRegistrations.Remove(extension);
                    foreach (Registration registration in registrations)
                        RetireRegistration_NoLock(registration);
                    retired.Add((extension, registrations));
                }

                var ownedByExtension = new Dictionary<TExtension, List<Registration>>(
                    ReferenceEqualityComparer.Instance);
                foreach ((TExtension extension, _) in candidates)
                {
                    if (!failures.ContainsKey(extension))
                        ownedByExtension.Add(extension, []);
                }

                foreach (AiJobSlotRegistrationMode mode in phases)
                {
                    foreach ((TExtension extension, TRegistration[] registrations) in candidates)
                    {
                        if (failures.ContainsKey(extension))
                            continue;

                        List<Registration> owned = ownedByExtension[extension];
                        foreach (TRegistration registration in registrations
                                     .Where(registration => _getMode(registration) == mode))
                        {
                            owned.Add(RegisterCore_NoLock(registration));
                        }
                    }
                }

                foreach ((TExtension extension, List<Registration> owned) in ownedByExtension)
                {
                    _extensionRegistrations[extension] = owned;
                }

                break;
            }
        }

        foreach ((TExtension extension, List<Registration> registrations) in retired)
        {
            ExtensionRegistrationLifetimes.Retire(
                extension,
                () => DisposeRegistrationsAsync(registrations));
        }

        foreach ((TExtension extension, Exception failure) in failures)
            ReportFailure(extension, failure);
    }

    private AiJobSlotRegistry<TCapability, TRegistration, TExtension> CreateScratchRegistry(
        IEnumerable<TRegistration> registrations)
        => new(
            registrations,
            _getKind,
            _getCapability,
            _getMode,
            _createRegistration,
            _getExtensionRegistrations,
            null,
            null);

    private TRegistration[] ValidateRegistrations(TExtension extension)
    {
        IReadOnlyCollection<TRegistration>? registrations = _getExtensionRegistrations(extension);
        if (registrations is null)
        {
            throw new InvalidOperationException(
                "An AI job capability extension returned a null registration collection.");
        }

        TRegistration[] snapshot = registrations.ToArray();
        if (snapshot.Any(registration => registration is null))
        {
            throw new InvalidOperationException(
                "An AI job capability extension returned a null registration.");
        }

        return snapshot;
    }

    private void ReportFailure(TExtension extension, Exception exception)
    {
        if (_reportFailure is null)
            return;

        try
        {
            _reportFailure(extension, exception);
        }
        catch
        {
            // Diagnostics must not interrupt extension removal before Extension.Unload().
        }
    }

    private Task UnregisterAsync(Registration registration, RegistrationState state)
    {
        Task drain;
        lock (_gate)
        {
            drain = state.RetireAsync();
            if (!drain.IsCompleted)
            {
                _activeRegistrationRetirements.Add(drain);
                _ = ForgetRetirementWhenCompleteAsync(drain);
            }

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

    private void RetireRegistration_NoLock(Registration registration)
    {
        (AiJobKindId Kind, Task Drain)? retired = registration.RetireNoLock();
        if (retired is null)
            return;

        if (_registrations.TryGetValue(retired.Value.Kind, out List<Registration>? registrations))
        {
            registrations.Remove(registration);
            if (registrations.Count == 0)
                _registrations.Remove(retired.Value.Kind);
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

    private static AiJobKindId Normalize(AiJobKindId kind)
        => new(kind.Value.ToLowerInvariant());

    private static void Validate(AiJobKindId kind, AiJobSlotRegistrationMode mode)
    {
        if (string.IsNullOrWhiteSpace(kind.Value))
            throw new ArgumentException("An AI job kind identifier is required.", nameof(kind));
        if (!Enum.IsDefined(mode))
            throw new ArgumentOutOfRangeException(nameof(mode));
    }

    private sealed class RegistrationState(AiJobKindId kind, TCapability capability)
    {
        private readonly object _gate = new();
        private TaskCompletionSource? _drained;
        private int _activeLeases;
        private bool _retired;

        public AiJobKindId Kind { get; } = kind;

        public TCapability Capability { get; } = capability;

        public bool TryAcquire([NotNullWhen(true)] out AiJobSlotLease<TCapability>? lease)
        {
            lock (_gate)
            {
                if (_retired)
                {
                    lease = null;
                    return false;
                }

                _activeLeases++;
                lease = new AiJobSlotLease<TCapability>(Capability, ReleaseLease);
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

        private void ReleaseLease()
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

    private sealed class Registration : IAiJobSlotRegistration
    {
        private readonly Lazy<Task> _retirement;
        private RegistrationOwner? _owner;

        public Registration(
            AiJobSlotRegistry<TCapability, TRegistration, TExtension> registry,
            RegistrationState state)
        {
            _owner = new RegistrationOwner(registry, state);
            _retirement = new Lazy<Task>(
                RetireCoreAsync,
                LazyThreadSafetyMode.ExecutionAndPublication);
        }

        public bool TryAcquire([NotNullWhen(true)] out AiJobSlotLease<TCapability>? lease)
        {
            RegistrationOwner? owner = Volatile.Read(ref _owner);
            if (_retirement.IsValueCreated || owner is null)
            {
                lease = null;
                return false;
            }

            return owner.State.TryAcquire(out lease);
        }

        public ValueTask DisposeAsync() => new(_retirement.Value);

        public (AiJobKindId Kind, Task Drain)? RetireNoLock()
        {
            RegistrationOwner? owner = Volatile.Read(ref _owner);
            return owner is null
                ? null
                : (owner.State.Kind, owner.State.RetireAsync());
        }

        public TRegistration ToSeedRegistration()
        {
            RegistrationOwner? owner = Volatile.Read(ref _owner);
            if (owner is null)
                throw new ObjectDisposedException(nameof(AiJobSlotRegistry<TCapability, TRegistration, TExtension>));

            return owner.Registry._createRegistration(
                owner.State.Kind,
                owner.State.Capability);
        }

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
            AiJobSlotRegistry<TCapability, TRegistration, TExtension> Registry,
            RegistrationState State);
    }
}

internal sealed class AiJobSlotLease<TCapability>(
    TCapability capability,
    Action release) : IDisposable
    where TCapability : class
{
    private Action? _release = release;

    public TCapability Capability
    {
        get
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _release) is null, this);
            return capability;
        }
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}
