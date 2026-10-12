namespace Beutl.ProjectSystem;

// LastWriteTimeUtcTicks is when this machine hashed the file. It is not saved, because it differs between
// machines and copies while identity uses only the length and the hash, so a fingerprint read from disk has 0
// there.
public sealed record MediaFileFingerprint(
    long Length,
    [property: System.Text.Json.Serialization.JsonIgnore] long LastWriteTimeUtcTicks,
    string Sha256)
{
    // Model sidecars use paths relative to the manifest, so a moved bundle keeps its identity.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, MediaFileFingerprint>? Dependencies { get; init; }
}
