using System.Text.Json.Nodes;

namespace Beutl.Serialization;

/// <summary>
/// Thread-local capture of fallback substitutions, letting a caller detect fallbacks created in
/// positions a hierarchical traversal of the deserialized result cannot reach (e.g. plain
/// property values such as keyframe values).
/// </summary>
internal static class DeserializationIncidents
{
    [ThreadStatic]
    private static Capture? t_capture;

    internal static Capture BeginCapture() => new(t_capture);

    // json is the JSON the fallback stands for, in the tree it was read from; a fallback for a missing type keeps that JSON.
    internal static void RecordFallback(IFallback? fallback = null, JsonObject? json = null)
    {
        Record(new DeserializationIncident(fallback, null, null, null, json ?? fallback?.Json));
        // A fallback keeps the JSON it stands in for, so a project read that made one kept data it cannot show.
        if (fallback is not null)
        {
            RootFileVersions.RecordKeptUnknownData();
        }
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
        for (Capture? capture = t_capture; capture != null; capture = capture.Parent)
        {
            capture.Record(incident);
        }
    }

    internal sealed class Capture : IDisposable
    {
        private List<DeserializationIncident>? _incidents;
        private bool _disposed;

        internal Capture(Capture? parent)
        {
            Parent = parent;
            t_capture = this;
        }

        internal Capture? Parent { get; }

        internal int Count => _incidents?.Count ?? 0;

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
        string? Message,
        JsonObject? Json = null);
}
