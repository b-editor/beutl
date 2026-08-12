using System.Diagnostics.CodeAnalysis;
using Beutl.Collections;
using Beutl.Editor.Models;

namespace Beutl.Editor.Services.Captions;

public enum CaptionTemplateRegistrationMode
{
    Add,
    Replace,
}

/// <summary>
/// Describes how an extension contribution is applied to the template registry.
/// </summary>
public sealed class CaptionTemplateRegistration
{
    public CaptionTemplateRegistration(
        CaptionTemplateContribution contribution,
        CaptionTemplateRegistrationMode mode = CaptionTemplateRegistrationMode.Add)
    {
        ArgumentNullException.ThrowIfNull(contribution);
        if (!Enum.IsDefined(mode))
            throw new ArgumentOutOfRangeException(nameof(mode));

        Contribution = contribution;
        Mode = mode;
    }

    public CaptionTemplateContribution Contribution { get; }

    public CaptionTemplateRegistrationMode Mode { get; }
}

/// <summary>
/// Describes one currently available caption template without retaining its package-owned
/// element factory or placement policy.
/// </summary>
public sealed record CaptionTemplateDescriptor
{
    internal CaptionTemplateDescriptor(CaptionTemplateContribution contribution)
    {
        Id = contribution.Id;
        ProviderId = contribution.ProviderId;
        Name = contribution.Name;
        Order = contribution.Order;
    }

    public CaptionTemplateId Id { get; }

    public CaptionTemplateProviderId ProviderId { get; }

    public string Name { get; }

    public int Order { get; }
}

/// <summary>
/// Holds a package-lifetime lease for one caption template. Dispose the lease after every
/// synchronous or asynchronous operation that may invoke template-created factories.
/// </summary>
public sealed class CaptionTemplateLease : IDisposable
{
    private readonly object _gate = new();
    private CaptionTemplateContribution? _contribution;
    private IDisposable? _lifetime;

    internal CaptionTemplateLease(
        CaptionTemplateContribution contribution,
        IDisposable lifetime)
    {
        _contribution = contribution;
        _lifetime = lifetime;
    }

    /// <summary>
    /// Creates descriptions that may contain package-owned factories. Fully materialize or discard
    /// the returned descriptions before disposing this lease.
    /// </summary>
    public IReadOnlyList<ElementDescription> CreateElements(
        CaptionCue cue,
        CaptionElementContext context)
    {
        lock (_gate)
            return GetContribution().CreateElements(cue, context);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _contribution = null;
            Interlocked.Exchange(ref _lifetime, null)?.Dispose();
        }
    }

    private CaptionTemplateContribution GetContribution()
        => _contribution ?? throw new ObjectDisposedException(nameof(CaptionTemplateLease));
}

/// <summary>
/// Resolves caption templates by stable identifier and applies explicit collision semantics.
/// Registry views contain metadata only; executable contributions are available through leases.
/// </summary>
public sealed class CaptionTemplateRegistry
{
    private readonly object _mutationGate = new();
    private readonly object _stateGate = new();
    private readonly CoreList<CaptionTemplateDescriptor> _templates = [];
    private State _state = State.Create([]);

    public CaptionTemplateRegistry()
    {
    }

    public CaptionTemplateRegistry(IEnumerable<CaptionTemplateContribution> contributions)
    {
        ArgumentNullException.ThrowIfNull(contributions);
        Replace(contributions.Select(contribution => new CaptionTemplateRegistration(contribution)));
    }

    public ICoreReadOnlyList<CaptionTemplateDescriptor> Templates => _templates;

    public void Register(
        CaptionTemplateContribution contribution,
        CaptionTemplateRegistrationMode mode = CaptionTemplateRegistrationMode.Add)
    {
        ArgumentNullException.ThrowIfNull(contribution);
        var registration = new CaptionTemplateRegistration(contribution, mode);
        lock (_mutationGate)
        {
            CaptionTemplateRegistration[] registrations;
            lock (_stateGate)
            {
                registrations =
                [
                    .. _state.Contributions.Select(item => new CaptionTemplateRegistration(item)),
                    registration,
                ];
            }

            SwapState(State.Create(registrations));
        }
    }

    /// <summary>
    /// Atomically replaces every registration and drains calls using the previous state.
    /// </summary>
    public void Replace(IEnumerable<CaptionTemplateRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        CaptionTemplateRegistration[] snapshot = registrations.ToArray();
        lock (_mutationGate)
        {
            SwapState(State.Create(snapshot));
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
            if (!state.ContributionsById.TryGetValue(id, out CaptionTemplateContribution? contribution))
            {
                throw new KeyNotFoundException(
                    $"No caption template is registered with identifier '{id}'.");
            }

            return new CaptionTemplateLease(contribution, state.AcquireLease());
        }
    }

    private void SwapState(State next)
    {
        State previous;
        lock (_stateGate)
        {
            previous = _state;
            _state = next;
            previous.Retire();
        }

        try
        {
            _templates.Replace(next.Templates);
        }
        catch
        {
            // The metadata state is already replaced. A UI observer must not prevent package
            // teardown from draining executable template leases.
        }
        finally
        {
            previous.WaitForLeases();
        }
    }

    private sealed class State : CaptionRegistryLeaseState
    {
        private State(Dictionary<CaptionTemplateId, CaptionTemplateContribution> contributions)
        {
            ContributionsById = contributions;
            Contributions = contributions.Values.ToArray();
            Templates = contributions.Values
                .Select(contribution => new CaptionTemplateDescriptor(contribution))
                .OrderBy(template => template.Order)
                .ThenBy(template => template.Id.Value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            TemplatesById = Templates.ToDictionary(template => template.Id);
        }

        public IReadOnlyList<CaptionTemplateContribution> Contributions { get; }

        public Dictionary<CaptionTemplateId, CaptionTemplateContribution> ContributionsById { get; }

        public CaptionTemplateDescriptor[] Templates { get; }

        public Dictionary<CaptionTemplateId, CaptionTemplateDescriptor> TemplatesById { get; }

        public static State Create(IEnumerable<CaptionTemplateRegistration> registrations)
        {
            var contributions = new Dictionary<CaptionTemplateId, CaptionTemplateContribution>();
            foreach (CaptionTemplateRegistration registration in registrations)
            {
                Apply(registration, contributions);
            }

            return new State(contributions);
        }

        private static void Apply(
            CaptionTemplateRegistration registration,
            Dictionary<CaptionTemplateId, CaptionTemplateContribution> contributions)
        {
            ArgumentNullException.ThrowIfNull(registration);
            CaptionTemplateContribution contribution = registration.Contribution;
            ArgumentNullException.ThrowIfNull(contribution);
            bool exists = contributions.TryGetValue(
                contribution.Id,
                out CaptionTemplateContribution? existing);
            switch (registration.Mode)
            {
                case CaptionTemplateRegistrationMode.Add when exists:
                    throw new ArgumentException(
                        $"Caption template '{contribution.Id}' is already registered by provider "
                        + $"'{existing!.ProviderId}'. Use Replace to override it explicitly.",
                        nameof(registration));
                case CaptionTemplateRegistrationMode.Replace when !exists:
                    throw new ArgumentException(
                        $"Caption template '{contribution.Id}' cannot be replaced because it is not registered.",
                        nameof(registration));
            }

            contributions[contribution.Id] = contribution;
        }
    }
}
