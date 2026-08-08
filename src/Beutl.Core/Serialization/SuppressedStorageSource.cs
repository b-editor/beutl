namespace Beutl;

/// <summary>
/// The retained on-disk bytes of an object the serializer must not regenerate, together with the
/// location those bytes came from. The source location is never rewritten; any other location
/// receives a verbatim copy.
/// </summary>
internal sealed record SuppressedStorageSource(
    byte[] RawBytes,
    Uri SourceUri,
    bool HasNonFallbackIncidents = false);
