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

    ICaptionTemplateLease Acquire(CaptionTemplateId id);
}

public sealed class CaptionTemplateRegistry : ICaptionTemplateProvider, IAsyncDisposable
{
    private readonly CoreList<CaptionTemplateDescriptor> _templates = [];
    private readonly CaptionRegistryStates<State> _states;

    public CaptionTemplateRegistry()
    {
        _states = CreateStates(State.Create([], [], []));
    }

    public CaptionTemplateRegistry(
        IEnumerable<CaptionTemplateDescriptorRegistration> descriptors,
        IEnumerable<CaptionElementFactoryRegistration> factories,
        IEnumerable<CaptionPlacementPolicyRegistration> placements)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        ArgumentNullException.ThrowIfNull(factories);
        ArgumentNullException.ThrowIfNull(placements);
        State state = State.Create(descriptors, factories, placements);
        _templates.Replace(state.Templates);
        _states = CreateStates(state);
    }

    public ICoreReadOnlyList<CaptionTemplateDescriptor> Templates => _templates;

    internal CaptionRegistryDrain ReplaceOwned(
        IEnumerable<CaptionTemplateDescriptorRegistration> descriptors,
        IEnumerable<CaptionElementFactoryRegistration> factories,
        IEnumerable<CaptionPlacementPolicyRegistration> placements,
        IReadOnlyDictionary<CaptionTemplateId, Extension> descriptorOwners,
        IReadOnlyDictionary<CaptionTemplateId, Extension> factoryOwners,
        IReadOnlyDictionary<CaptionTemplateId, Extension> placementOwners)
        => _states.Replace(() => State.Create(
            descriptors,
            factories,
            placements,
            ToObjectOwners(descriptorOwners),
            ToObjectOwners(factoryOwners),
            ToObjectOwners(placementOwners)));

    public bool TryGet(
        CaptionTemplateId id,
        [NotNullWhen(true)] out CaptionTemplateDescriptor? template)
    {
        lock (_states.StateGate)
            return _states.Current.TemplatesById.TryGetValue(id, out template);
    }

    public CaptionTemplateLease Acquire(CaptionTemplateId id)
    {
        lock (_states.StateGate)
        {
            State state = _states.Current;
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

    public ValueTask DisposeAsync() => _states.DisposeAsync();

    private CaptionRegistryStates<State> CreateStates(State initial)
        => new(this, initial, static () => State.Create([], [], []), PublishMetadata);

    private void PublishMetadata(State state) => _templates.Replace(state.Templates);

    private static IReadOnlyDictionary<CaptionTemplateId, object> ToObjectOwners(
        IReadOnlyDictionary<CaptionTemplateId, Extension> owners)
        => owners.ToDictionary(pair => pair.Key, pair => (object)pair.Value);

    private sealed class State : CaptionRegistryLeaseState
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

        protected override void ReleasePayload()
        {
            Compositions = [];
            Templates = [];
            TemplatesById = [];
            DescriptorOwners = new Dictionary<CaptionTemplateId, object>();
            FactoryOwners = new Dictionary<CaptionTemplateId, object>();
            PlacementOwners = new Dictionary<CaptionTemplateId, object>();
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
                ApplyRegistration(
                    descriptors,
                    registration.Descriptor.Id,
                    registration.Descriptor,
                    registration.Mode == CaptionTemplateRegistrationMode.Replace,
                    "Caption template",
                    "descriptor");
            }
            foreach (CaptionElementFactoryRegistration registration in factoryRegistrations)
            {
                ApplyRegistration(
                    factories,
                    registration.TemplateId,
                    registration.Factory,
                    registration.Mode == CaptionTemplateRegistrationMode.Replace,
                    "Caption template",
                    "element factory");
            }
            foreach (CaptionPlacementPolicyRegistration registration in placementRegistrations)
            {
                ApplyRegistration(
                    placements,
                    registration.TemplateId,
                    registration.Policy,
                    registration.Mode == CaptionTemplateRegistrationMode.Replace,
                    "Caption template",
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
    }
}
