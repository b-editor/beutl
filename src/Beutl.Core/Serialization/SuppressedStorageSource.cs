namespace Beutl;

/// <summary>
/// The retained on-disk content of an object the serializer must not regenerate, together with the
/// location those bytes came from.
/// </summary>
internal sealed record SuppressedStorageSource(string RawText, Uri SourceUri);
