namespace Beutl.Graphics.Effects;

internal sealed class GeometryStructuralIdentity(
    object key,
    object bounds,
    object hitTest,
    bool requiresReadback,
    object inputDemand,
    Type[] resourceTypes)
    : IEquatable<GeometryStructuralIdentity>
{
    public bool Equals(GeometryStructuralIdentity? other)
        => other is not null
           && Equals(key, other.Key)
           && Equals(bounds, other.Bounds)
           && Equals(hitTest, other.HitTest)
           && requiresReadback == other.RequiresReadback
           && Equals(inputDemand, other.InputDemand)
           && resourceTypes.AsSpan().SequenceEqual(other.ResourceTypes);

    public override bool Equals(object? obj) => obj is GeometryStructuralIdentity other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(key);
        hash.Add(bounds);
        hash.Add(hitTest);
        hash.Add(requiresReadback);
        hash.Add(inputDemand);
        foreach (Type resourceType in resourceTypes)
        {
            hash.Add(resourceType);
        }
        return hash.ToHashCode();
    }

    private object Key => key;
    private object Bounds => bounds;
    private object HitTest => hitTest;
    private bool RequiresReadback => requiresReadback;
    private object InputDemand => inputDemand;
    private Type[] ResourceTypes => resourceTypes;
}
