namespace Beutl.Serialization;

/// <summary>
/// Thread-local tally of fallback substitutions, letting a caller detect fallbacks created in
/// positions a hierarchical traversal of the deserialized result cannot reach (e.g. plain
/// property values such as keyframe values).
/// </summary>
internal static class DeserializationIncidents
{
    [ThreadStatic]
    private static int t_fallbackCount;

    internal static int FallbackCount => t_fallbackCount;

    /// <summary>
/// Records a fallback substitution for the current thread.
/// </summary>
internal static void RecordFallback() => t_fallbackCount++;
}
