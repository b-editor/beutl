namespace Beutl;

internal static class ValueReplacement
{
    public static bool RequiresReplacement<T>(T current, T candidate, bool replaceEquivalent)
    {
        return replaceEquivalent && !typeof(T).IsValueType
            ? !ReferenceEquals(current, candidate)
            : !EqualityComparer<T>.Default.Equals(current, candidate);
    }
}
