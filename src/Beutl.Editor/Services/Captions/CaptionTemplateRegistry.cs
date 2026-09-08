using System.Diagnostics.CodeAnalysis;
using Beutl.Collections;
using Beutl.Editor.Models;
using Beutl.Extensibility;

namespace Beutl.Editor.Services.Captions;

public enum CaptionTemplateRegistrationMode
{
    Add,
    Replace,
}

/// <summary>Describes one currently available caption template.</summary>
public sealed record CaptionTemplateDescriptor
{
    public CaptionTemplateDescriptor(
        CaptionTemplateId id,
        CaptionTemplateProviderId providerId,
        string name,
        int order = 0)
    {
        if (id.Value.Length == 0)
            throw new ArgumentException("A caption template identifier is required.", nameof(id));
        if (providerId.Value.Length == 0)
            throw new ArgumentException("A caption template provider identifier is required.", nameof(providerId));
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Id = id;
        ProviderId = providerId;
        Name = name;
        Order = order;
    }

    public CaptionTemplateId Id { get; }

    public CaptionTemplateProviderId ProviderId { get; }

    public string Name { get; }

    public int Order { get; }
}

public sealed class CaptionTemplateDescriptorRegistration
{
    public CaptionTemplateDescriptorRegistration(
        CaptionTemplateDescriptor descriptor,
        CaptionTemplateRegistrationMode mode = CaptionTemplateRegistrationMode.Add)
    {
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        if (!Enum.IsDefined(mode))
            throw new ArgumentOutOfRangeException(nameof(mode));
        Mode = mode;
    }

    public CaptionTemplateDescriptor Descriptor { get; }

    public CaptionTemplateRegistrationMode Mode { get; }
}

public sealed class CaptionElementFactoryRegistration
{
    public CaptionElementFactoryRegistration(
        CaptionTemplateId templateId,
        ICaptionElementFactory factory,
        CaptionTemplateRegistrationMode mode = CaptionTemplateRegistrationMode.Add)
    {
        if (templateId.Value.Length == 0)
            throw new ArgumentException("A caption template identifier is required.", nameof(templateId));
        TemplateId = templateId;
        Factory = factory ?? throw new ArgumentNullException(nameof(factory));
        if (!Enum.IsDefined(mode))
            throw new ArgumentOutOfRangeException(nameof(mode));
        Mode = mode;
    }

    public CaptionTemplateId TemplateId { get; }

    public ICaptionElementFactory Factory { get; }

    public CaptionTemplateRegistrationMode Mode { get; }
}

public sealed class CaptionPlacementPolicyRegistration
{
    public CaptionPlacementPolicyRegistration(
        CaptionTemplateId templateId,
        ICaptionPlacementPolicy policy,
        CaptionTemplateRegistrationMode mode = CaptionTemplateRegistrationMode.Add)
    {
        if (templateId.Value.Length == 0)
            throw new ArgumentException("A caption template identifier is required.", nameof(templateId));
        TemplateId = templateId;
        Policy = policy ?? throw new ArgumentNullException(nameof(policy));
        if (!Enum.IsDefined(mode))
            throw new ArgumentOutOfRangeException(nameof(mode));
        Mode = mode;
    }

    public CaptionTemplateId TemplateId { get; }

    public ICaptionPlacementPolicy Policy { get; }

    public CaptionTemplateRegistrationMode Mode { get; }
}

public interface ICaptionTemplateDescriptorRegistration : IAsyncDisposable
{
}

public interface ICaptionElementFactoryRegistration : IAsyncDisposable
{
}

public interface ICaptionPlacementPolicyRegistration : IAsyncDisposable
{
}

/// <summary>
/// Holds one coherent descriptor/factory/placement snapshot. Materialize or discard every returned
/// description before disposing the lease.
/// </summary>
public interface ICaptionTemplateLease : IDisposable
{
    IReadOnlyList<ElementDescription> CreateElements(
        CaptionCue cue,
        CaptionElementContext context);
}

public sealed class CaptionTemplateLease : ICaptionTemplateLease
{
    private readonly object _gate = new();
    private CaptionTemplateComposition? _composition;
    private IDisposable? _lifetime;

    internal CaptionTemplateLease(
        CaptionTemplateComposition composition,
        IDisposable lifetime)
    {
        _composition = composition;
        _lifetime = lifetime;
    }

    public IReadOnlyList<ElementDescription> CreateElements(
        CaptionCue cue,
        CaptionElementContext context)
    {
        lock (_gate)
        {
            return (_composition
                    ?? throw new ObjectDisposedException(nameof(CaptionTemplateLease)))
                .CreateElements(cue, context);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _composition = null;
            Interlocked.Exchange(ref _lifetime, null)?.Dispose();
        }
    }
}

public interface ICaptionTemplateProvider
{
    ICoreReadOnlyList<CaptionTemplateDescriptor> Templates { get; }

    bool TryGet(
        CaptionTemplateId id,
        [NotNullWhen(true)] out CaptionTemplateDescriptor? template);

    CaptionTemplateDescriptor GetRequired(CaptionTemplateId id);

    ICaptionTemplateLease Acquire(CaptionTemplateId id);
}

public sealed class CaptionTemplateRegistry : ICaptionTemplateProvider, IAsyncDisposable
{
    private readonly object _mutationGate = new();
    private readonly object _stateGate = new();
    private readonly CoreList<CaptionTemplateDescriptor> _templates = [];
    private readonly List<DescriptorEntry> _descriptorEntries = [];
    private readonly List<FactoryEntry> _factoryEntries = [];
    private readonly List<PlacementEntry> _placementEntries = [];
    private readonly HashSet<State> _retiredStates = [];
    private State _state = State.Create([], [], []);
    private Task? _disposeTask;
    private bool _disposed;

    public CaptionTemplateRegistry()
    {
    }

    public CaptionTemplateRegistry(
        IEnumerable<CaptionTemplateDescriptorRegistration> descriptors,
        IEnumerable<CaptionElementFactoryRegistration> factories,
        IEnumerable<CaptionPlacementPolicyRegistration> placements)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        ArgumentNullException.ThrowIfNull(factories);
        ArgumentNullException.ThrowIfNull(placements);
        _descriptorEntries.AddRange(descriptors.Select(registration => new DescriptorEntry(registration)));
        _factoryEntries.AddRange(factories.Select(registration => new FactoryEntry(registration)));
        _placementEntries.AddRange(placements.Select(registration => new PlacementEntry(registration)));
        _state = State.Create(
            _descriptorEntries.Select(entry => entry.Registration),
            _factoryEntries.Select(entry => entry.Registration),
            _placementEntries.Select(entry => entry.Registration));
        _templates.Replace(_state.Templates);
    }

    public ICoreReadOnlyList<CaptionTemplateDescriptor> Templates => _templates;

    public ICaptionTemplateDescriptorRegistration Register(
        CaptionTemplateDescriptorRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        lock (_mutationGate)
        {
            ThrowIfDisposed();
            ValidateRegistrationMode(
                registration.Descriptor.Id,
                registration.Mode,
                _descriptorEntries.Any(entry =>
                    entry.Registration.Descriptor.Id == registration.Descriptor.Id),
                "descriptor");
            var entry = new DescriptorEntry(registration);
            _descriptorEntries.Add(entry);
            try
            {
                SwapState(CreateDirectState());
                return new DescriptorHandle(this, entry);
            }
            catch
            {
                _descriptorEntries.Remove(entry);
                throw;
            }
        }
    }

    public ICaptionElementFactoryRegistration Register(CaptionElementFactoryRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        lock (_mutationGate)
        {
            ThrowIfDisposed();
            ValidateRegistrationMode(
                registration.TemplateId,
                registration.Mode,
                _factoryEntries.Any(entry =>
                    entry.Registration.TemplateId == registration.TemplateId),
                "element factory");
            var entry = new FactoryEntry(registration);
            _factoryEntries.Add(entry);
            try
            {
                SwapState(CreateDirectState());
                return new FactoryHandle(this, entry);
            }
            catch
            {
                _factoryEntries.Remove(entry);
                throw;
            }
        }
    }

    public ICaptionPlacementPolicyRegistration Register(
        CaptionPlacementPolicyRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        lock (_mutationGate)
        {
            ThrowIfDisposed();
            ValidateRegistrationMode(
                registration.TemplateId,
                registration.Mode,
                _placementEntries.Any(entry =>
                    entry.Registration.TemplateId == registration.TemplateId),
                "placement policy");
            var entry = new PlacementEntry(registration);
            _placementEntries.Add(entry);
            try
            {
                SwapState(CreateDirectState());
                return new PlacementHandle(this, entry);
            }
            catch
            {
                _placementEntries.Remove(entry);
                throw;
            }
        }
    }

    internal CaptionRegistryDrain<Extension> ReplaceOwned(
        IEnumerable<CaptionTemplateDescriptorRegistration> descriptors,
        IEnumerable<CaptionElementFactoryRegistration> factories,
        IEnumerable<CaptionPlacementPolicyRegistration> placements,
        IReadOnlyDictionary<CaptionTemplateId, Extension> descriptorOwners,
        IReadOnlyDictionary<CaptionTemplateId, Extension> factoryOwners,
        IReadOnlyDictionary<CaptionTemplateId, Extension> placementOwners)
    {
        lock (_mutationGate)
        {
            ThrowIfDisposed();
            CaptionRegistryDrain<object> retired = SwapState(State.Create(
                descriptors,
                factories,
                placements,
                ToObjectOwners(descriptorOwners),
                ToObjectOwners(factoryOwners),
                ToObjectOwners(placementOwners)));
            return new CaptionRegistryDrain<Extension>(
                retired.All.AsTask(),
                owner => retired.DrainOwnerAsync(owner));
        }
    }

    public bool TryGet(
        CaptionTemplateId id,
        [NotNullWhen(true)] out CaptionTemplateDescriptor? template)
    {
        lock (_stateGate)
            return _state.TemplatesById.TryGetValue(id, out template);
    }

    public CaptionTemplateDescriptor GetRequired(CaptionTemplateId id)
    {
        if (TryGet(id, out CaptionTemplateDescriptor? template))
            return template;
        throw new KeyNotFoundException($"No caption template is registered with identifier '{id}'.");
    }

    public CaptionTemplateLease Acquire(CaptionTemplateId id)
    {
        lock (_stateGate)
        {
            State state = _state;
            if (!state.Compositions.TryGetValue(id, out CaptionTemplateComposition? composition))
            {
                throw new KeyNotFoundException(
                    $"No complete caption template is registered with identifier '{id}'.");
            }

            return new CaptionTemplateLease(
                composition,
                state.AcquireLease(state.GetOwners(id)));
        }
    }

    ICaptionTemplateLease ICaptionTemplateProvider.Acquire(CaptionTemplateId id) => Acquire(id);

    public ValueTask DisposeAsync()
    {
        lock (_mutationGate)
        {
            return new ValueTask(_disposeTask ??= DisposeCoreAsync());
        }
    }

    private Task DisposeCoreAsync()
    {
        if (_disposed)
            return Task.CompletedTask;
        _disposed = true;
        _descriptorEntries.Clear();
        _factoryEntries.Clear();
        _placementEntries.Clear();
        CaptionRegistryDrain<object> retired = SwapState(State.Create([], [], []));
        return Task.WhenAll(
            _retiredStates.Select(state => state.RetireAsync())
                .Append(retired.All.AsTask())
                .Distinct());
    }

    private State CreateDirectState()
    {
        DescriptorEntry[] descriptors = _descriptorEntries
            .GroupBy(entry => entry.Registration.Descriptor.Id)
            .Select(group => group.Last())
            .ToArray();
        FactoryEntry[] factories = _factoryEntries
            .GroupBy(entry => entry.Registration.TemplateId)
            .Select(group => group.Last())
            .ToArray();
        PlacementEntry[] placements = _placementEntries
            .GroupBy(entry => entry.Registration.TemplateId)
            .Select(group => group.Last())
            .ToArray();
        return State.Create(
            descriptors.Select(entry => new CaptionTemplateDescriptorRegistration(
                entry.Registration.Descriptor)),
            factories.Select(entry => new CaptionElementFactoryRegistration(
                entry.Registration.TemplateId,
                entry.Registration.Factory)),
            placements.Select(entry => new CaptionPlacementPolicyRegistration(
                entry.Registration.TemplateId,
                entry.Registration.Policy)),
            descriptors.ToDictionary(
                entry => entry.Registration.Descriptor.Id,
                entry => (object)entry),
            factories.ToDictionary(
                entry => entry.Registration.TemplateId,
                entry => (object)entry),
            placements.ToDictionary(
                entry => entry.Registration.TemplateId,
                entry => (object)entry));
    }

    private CaptionRegistryDrain<object> SwapState(State next)
    {
        State previous;
        lock (_stateGate)
        {
            previous = _state;
            _state = next;
        }

        try
        {
            _templates.Replace(next.Templates);
        }
        catch
        {
        }

        Task retired = previous.RetireAndReleasePayloadAsync();
        if (!retired.IsCompleted)
        {
            _retiredStates.Add(previous);
            _ = retired.ContinueWith(
                _ => RemoveRetiredState(previous),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        return new CaptionRegistryDrain<object>(
            retired,
            previous.DrainOwnerAsync);
    }

    private void RemoveRetiredState(State state)
    {
        lock (_mutationGate)
            _retiredStates.Remove(state);
    }

    private Task RemoveAsync(DescriptorEntry entry)
    {
        lock (_mutationGate)
        {
            if (!_descriptorEntries.Remove(entry))
                return _disposeTask ?? Task.CompletedTask;
            CaptionRegistryDrain<object> retired = SwapState(CreateDirectState());
            return DrainOwnerAcrossRetiredStates(entry, retired);
        }
    }

    private Task RemoveAsync(FactoryEntry entry)
    {
        lock (_mutationGate)
        {
            if (!_factoryEntries.Remove(entry))
                return _disposeTask ?? Task.CompletedTask;
            CaptionRegistryDrain<object> retired = SwapState(CreateDirectState());
            return DrainOwnerAcrossRetiredStates(entry, retired);
        }
    }

    private Task RemoveAsync(PlacementEntry entry)
    {
        lock (_mutationGate)
        {
            if (!_placementEntries.Remove(entry))
                return _disposeTask ?? Task.CompletedTask;
            CaptionRegistryDrain<object> retired = SwapState(CreateDirectState());
            return DrainOwnerAcrossRetiredStates(entry, retired);
        }
    }

    private Task DrainOwnerAcrossRetiredStates(object owner, CaptionRegistryDrain<object> retired)
        => Task.WhenAll(
            _retiredStates.Select(state => state.DrainOwnerAsync(owner))
                .Append(retired.DrainOwnerAsync(owner))
                .Distinct());

    private static IReadOnlyDictionary<CaptionTemplateId, object> ToObjectOwners(
        IReadOnlyDictionary<CaptionTemplateId, Extension> owners)
        => owners.ToDictionary(pair => pair.Key, pair => (object)pair.Value);

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private static void ValidateRegistrationMode(
        CaptionTemplateId id,
        CaptionTemplateRegistrationMode mode,
        bool exists,
        string capabilityName)
    {
        if (mode == CaptionTemplateRegistrationMode.Add && exists)
        {
            throw new ArgumentException(
                $"Caption template '{id}' already has a {capabilityName}. Use Replace explicitly.");
        }
        if (mode == CaptionTemplateRegistrationMode.Replace && !exists)
        {
            throw new ArgumentException(
                $"Caption template '{id}' has no {capabilityName} to replace.");
        }
    }

    private sealed class State : CaptionRegistryLeaseState<object>
    {
        private State(
            Dictionary<CaptionTemplateId, CaptionTemplateDescriptor> descriptors,
            Dictionary<CaptionTemplateId, ICaptionElementFactory> factories,
            Dictionary<CaptionTemplateId, ICaptionPlacementPolicy> placements,
            IReadOnlyDictionary<CaptionTemplateId, object> descriptorOwners,
            IReadOnlyDictionary<CaptionTemplateId, object> factoryOwners,
            IReadOnlyDictionary<CaptionTemplateId, object> placementOwners)
        {
            DescriptorOwners = descriptorOwners;
            FactoryOwners = factoryOwners;
            PlacementOwners = placementOwners;
            Compositions = descriptors.Keys
                .Where(factories.ContainsKey)
                .Where(placements.ContainsKey)
                .ToDictionary(
                    id => id,
                    id => new CaptionTemplateComposition(
                        descriptors[id],
                        factories[id],
                        placements[id]));
            Templates = Compositions.Values
                .Select(composition => composition.Descriptor)
                .OrderBy(template => template.Order)
                .ThenBy(template => template.Id.Value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            TemplatesById = Templates.ToDictionary(template => template.Id);
        }

        public Dictionary<CaptionTemplateId, CaptionTemplateComposition> Compositions
        { get; private set; }

        public CaptionTemplateDescriptor[] Templates { get; private set; }

        public Dictionary<CaptionTemplateId, CaptionTemplateDescriptor> TemplatesById
        { get; private set; }

        public IReadOnlyDictionary<CaptionTemplateId, object> DescriptorOwners { get; private set; }

        public IReadOnlyDictionary<CaptionTemplateId, object> FactoryOwners { get; private set; }

        public IReadOnlyDictionary<CaptionTemplateId, object> PlacementOwners { get; private set; }

        public IReadOnlyCollection<object> GetOwners(CaptionTemplateId id)
        {
            var owners = new HashSet<object>(ReferenceEqualityComparer.Instance);
            if (DescriptorOwners.TryGetValue(id, out object? descriptorOwner))
                owners.Add(descriptorOwner);
            if (FactoryOwners.TryGetValue(id, out object? factoryOwner))
                owners.Add(factoryOwner);
            if (PlacementOwners.TryGetValue(id, out object? placementOwner))
                owners.Add(placementOwner);
            return owners.ToArray();
        }

        public Task RetireAndReleasePayloadAsync()
        {
            Task drain = RetireAsync();
            Compositions = [];
            Templates = [];
            TemplatesById = [];
            DescriptorOwners = new Dictionary<CaptionTemplateId, object>();
            FactoryOwners = new Dictionary<CaptionTemplateId, object>();
            PlacementOwners = new Dictionary<CaptionTemplateId, object>();
            return drain;
        }

        public static State Create(
            IEnumerable<CaptionTemplateDescriptorRegistration> descriptorRegistrations,
            IEnumerable<CaptionElementFactoryRegistration> factoryRegistrations,
            IEnumerable<CaptionPlacementPolicyRegistration> placementRegistrations,
            IReadOnlyDictionary<CaptionTemplateId, object>? descriptorOwners = null,
            IReadOnlyDictionary<CaptionTemplateId, object>? factoryOwners = null,
            IReadOnlyDictionary<CaptionTemplateId, object>? placementOwners = null)
        {
            var descriptors = new Dictionary<CaptionTemplateId, CaptionTemplateDescriptor>();
            var factories = new Dictionary<CaptionTemplateId, ICaptionElementFactory>();
            var placements = new Dictionary<CaptionTemplateId, ICaptionPlacementPolicy>();
            foreach (CaptionTemplateDescriptorRegistration registration in descriptorRegistrations)
            {
                Apply(
                    registration.Descriptor.Id,
                    registration.Descriptor,
                    registration.Mode,
                    descriptors,
                    "descriptor");
            }
            foreach (CaptionElementFactoryRegistration registration in factoryRegistrations)
            {
                Apply(
                    registration.TemplateId,
                    registration.Factory,
                    registration.Mode,
                    factories,
                    "element factory");
            }
            foreach (CaptionPlacementPolicyRegistration registration in placementRegistrations)
            {
                Apply(
                    registration.TemplateId,
                    registration.Policy,
                    registration.Mode,
                    placements,
                    "placement policy");
            }

            return new State(
                descriptors,
                factories,
                placements,
                descriptorOwners ?? new Dictionary<CaptionTemplateId, object>(),
                factoryOwners ?? new Dictionary<CaptionTemplateId, object>(),
                placementOwners ?? new Dictionary<CaptionTemplateId, object>());
        }

        private static void Apply<TCapability>(
            CaptionTemplateId id,
            TCapability capability,
            CaptionTemplateRegistrationMode mode,
            Dictionary<CaptionTemplateId, TCapability> capabilities,
            string capabilityName)
        {
            bool exists = capabilities.ContainsKey(id);
            if (mode == CaptionTemplateRegistrationMode.Add && exists)
            {
                throw new ArgumentException(
                    $"Caption template '{id}' already has a {capabilityName}. Use Replace explicitly.");
            }
            if (mode == CaptionTemplateRegistrationMode.Replace && !exists)
            {
                throw new ArgumentException(
                    $"Caption template '{id}' has no {capabilityName} to replace.");
            }
            capabilities[id] = capability;
        }
    }

    private sealed class DescriptorEntry(CaptionTemplateDescriptorRegistration registration)
    {
        public CaptionTemplateDescriptorRegistration Registration { get; } = registration;
    }

    private sealed class FactoryEntry(CaptionElementFactoryRegistration registration)
    {
        public CaptionElementFactoryRegistration Registration { get; } = registration;
    }

    private sealed class PlacementEntry(CaptionPlacementPolicyRegistration registration)
    {
        public CaptionPlacementPolicyRegistration Registration { get; } = registration;
    }

    private abstract class Handle
    {
        private readonly Lazy<Task> _dispose;

        protected Handle(Func<Task> dispose)
        {
            _dispose = new Lazy<Task>(dispose, LazyThreadSafetyMode.ExecutionAndPublication);
        }

        protected ValueTask DisposeCoreAsync() => new(_dispose.Value);
    }

    private sealed class DescriptorHandle(
        CaptionTemplateRegistry registry,
        DescriptorEntry entry) : Handle(() => registry.RemoveAsync(entry)),
        ICaptionTemplateDescriptorRegistration
    {
        public ValueTask DisposeAsync() => DisposeCoreAsync();
    }

    private sealed class FactoryHandle(
        CaptionTemplateRegistry registry,
        FactoryEntry entry) : Handle(() => registry.RemoveAsync(entry)),
        ICaptionElementFactoryRegistration
    {
        public ValueTask DisposeAsync() => DisposeCoreAsync();
    }

    private sealed class PlacementHandle(
        CaptionTemplateRegistry registry,
        PlacementEntry entry) : Handle(() => registry.RemoveAsync(entry)),
        ICaptionPlacementPolicyRegistration
    {
        public ValueTask DisposeAsync() => DisposeCoreAsync();
    }
}
