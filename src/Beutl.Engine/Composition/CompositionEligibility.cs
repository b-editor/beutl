using System.Collections.Immutable;
using Beutl.Engine;

namespace Beutl.Composition;

/// <summary>
/// Captures the original objects that are currently eligible for a composition target, independently
/// of whether their time ranges intersect the evaluated frame.
/// </summary>
/// <remarks>
/// Original objects are identity tokens only. Membership always uses reference equality so a plugin
/// type overriding <see cref="object.Equals(object?)"/> cannot alias another object.
/// </remarks>
public readonly struct CompositionEligibility
{
    private readonly ImmutableHashSet<EngineObject>? _objects;

    /// <summary>Creates an immutable eligibility snapshot from original object identities.</summary>
    /// <param name="objects">Objects eligible for the frame's composition target.</param>
    public CompositionEligibility(IEnumerable<EngineObject> objects)
    {
        ArgumentNullException.ThrowIfNull(objects);
        _objects = objects.ToImmutableHashSet<EngineObject>(ReferenceEqualityComparer.Instance);
    }

    /// <summary>Gets a snapshot in which no objects are eligible.</summary>
    public static CompositionEligibility Empty => default;

    /// <summary>Gets the number of eligible object identities.</summary>
    public int Count => _objects?.Count ?? 0;

    /// <summary>Determines whether the snapshot contains the exact <paramref name="obj"/> instance.</summary>
    public bool Contains(EngineObject obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        return _objects?.Contains(obj) ?? false;
    }
}
