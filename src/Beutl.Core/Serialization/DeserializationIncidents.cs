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

    [ThreadStatic]
    private static Capture? t_capture;

    internal static int FallbackCount => t_fallbackCount;

    internal static Capture BeginCapture() => new(t_capture);

    internal static void RecordFallback(IFallback? fallback = null)
    {
        Record(new DeserializationIncident(fallback, null, null, null));
    }

    internal static void RecordFallback(
        FallbackReason reason,
        string? typeName,
        string? message)
    {
        Record(new DeserializationIncident(null, reason, typeName, message));
    }

    private static void Record(DeserializationIncident incident)
    {
        t_fallbackCount++;
        for (Capture? capture = t_capture; capture != null; capture = capture.Parent)
        {
            capture.Record(incident);
        }
    }

    internal sealed class Capture : IDisposable
    {
        private readonly int _initialCount;
        private List<DeserializationIncident>? _incidents;
        private bool _disposed;

        internal Capture(Capture? parent)
        {
            Parent = parent;
            _initialCount = t_fallbackCount;
            t_capture = this;
        }

        internal Capture? Parent { get; }

        internal int Count => t_fallbackCount - _initialCount;

        internal IReadOnlyList<DeserializationIncident> Incidents => _incidents ?? [];

        internal void Record(DeserializationIncident incident)
            => (_incidents ??= []).Add(incident);

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            if (!ReferenceEquals(t_capture, this))
            {
                throw new InvalidOperationException("Deserialization incident captures must be disposed in stack order.");
            }

            t_capture = Parent;
            _disposed = true;
        }
    }

    internal sealed record DeserializationIncident(
        IFallback? Fallback,
        FallbackReason? Reason,
        string? TypeName,
        string? Message);
}
