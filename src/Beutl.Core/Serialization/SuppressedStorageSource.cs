using System.Text.Json.Nodes;

namespace Beutl;

/// <summary>
/// The retained on-disk bytes of an object the serializer must not regenerate, together with the
/// location those bytes came from. The source location is never rewritten; any other location
/// receives a verbatim copy.
/// </summary>
internal sealed record SuppressedStorageSource(
    byte[] RawBytes,
    Uri SourceUri,
    bool HasNonFallbackIncidents = false,
    JsonObject[]? UntraversedFallbacks = null,
    SuppressedReferencedStorageSource[]? ReferencedStorageSources = null)
{
    /// <summary>
    /// True when this suppression record was put back by undoing an in-process repair. Only a
    /// reinstated record may restore the retained bytes over a mismatched sidecar; a continuously
    /// held record treats a mismatch as an external repair and leaves the changed file alone.
    /// Cleared once the retained bytes have been restored.
    /// </summary>
    public bool WasReinstated { get; set; }
}

internal sealed record SuppressedReferencedStorageSource(
    byte[] RawBytes,
    string RelativeUri);
