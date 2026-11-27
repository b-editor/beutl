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
    [ThreadStatic]
    private static JsonNode? t_sourceJson;

    internal static Capture BeginCapture() => new(t_capture);

    // A JSON converter can deserialize from a fresh JSON tree. Retain the entry point into the
    // original tree so recovery can still find the member that contained a dropped value.
    internal static SourceScope PreserveSource(JsonNode source)
        => t_capture is not null && t_sourceJson is null ? new(source) : default;

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
        string? message,
        CoreObject owner)
    {
        Record(new DeserializationIncident(null, reason, typeName, message, Owner: owner));
    }

    private static void Record(DeserializationIncident incident)
    {
        if (t_sourceJson is { } source) incident = incident with { SourceJson = source };
        for (Capture? capture = t_capture; capture != null; capture = capture.Parent)
        {
            capture.Record(incident);
        }
    }

    internal readonly struct SourceScope : IDisposable
    {
        private readonly bool _ownsSource;

        public SourceScope(JsonNode source)
        {
            t_sourceJson = source;
            _ownsSource = true;
        }

        public void Dispose()
        {
            if (_ownsSource) t_sourceJson = null;
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
        JsonObject? Json = null,
        CoreObject? Owner = null,
        JsonNode? SourceJson = null);
}
