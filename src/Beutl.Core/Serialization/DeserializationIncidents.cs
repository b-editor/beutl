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
        t_fallbackCount++;
        for (Capture? capture = t_capture; capture != null; capture = capture.Parent)
        {
            capture.Record(fallback);
        }
    }

    internal sealed class Capture : IDisposable
    {
        private readonly int _initialCount;
        private List<IFallback>? _fallbacks;
        private bool _disposed;

        internal Capture(Capture? parent)
        {
            Parent = parent;
            _initialCount = t_fallbackCount;
            t_capture = this;
        }

        internal Capture? Parent { get; }

        internal int Count => t_fallbackCount - _initialCount;

        internal IReadOnlyList<IFallback> Fallbacks => _fallbacks ?? [];

        internal void Record(IFallback? fallback)
        {
            if (fallback != null)
            {
                (_fallbacks ??= []).Add(fallback);
            }
        }

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
}
