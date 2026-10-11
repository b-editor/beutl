namespace Beutl.ProjectSystem;

// LastWriteTimeUtcTicks is the file's last-write time when this machine hashed it. It is not saved:
// it differs between machines and copies of a project, and identity uses only the length and the
// hash, so saving it rewrote the shared fingerprints when only timestamps changed. A fingerprint read
// from disk has 0 there, including one an earlier build wrote with the value. The fingerprints of a
// scene are saved next to it (MediaFingerprintFile).
public sealed record MediaFileFingerprint(
    long Length,
    [property: System.Text.Json.Serialization.JsonIgnore] long LastWriteTimeUtcTicks,
    string Sha256)
{
    // Model sidecars use paths relative to the manifest, so a moved bundle keeps its identity.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, MediaFileFingerprint>? Dependencies { get; init; }
}
