namespace Beutl.ProjectSystem;

public sealed record MediaFileFingerprint(long Length, long LastWriteTimeUtcTicks, string Sha256)
{
    // Model sidecars use paths relative to the manifest, so a moved bundle keeps its identity.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, MediaFileFingerprint>? Dependencies { get; init; }
}
