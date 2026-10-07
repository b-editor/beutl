namespace Beutl.ProjectSystem;

public sealed record MediaFileFingerprint(long Length, long LastWriteTimeUtcTicks, string Sha256);
