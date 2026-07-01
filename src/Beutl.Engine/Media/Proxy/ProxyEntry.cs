using Beutl.Media;

namespace Beutl.Media.Proxy;

public sealed record ProxyEntry(
    ProxyFingerprint Source,
    ProxyPreset Preset,
    ProxyState State,
    string ProxyFileRelative,
    long ProxyFileSizeBytes,
    PixelSize OriginalLogicalFrameSize,
    PixelSize ProxyDecodedFrameSize,
    DateTime GeneratedAtUtc,
    DateTime LastUsedUtc,
    string? FailureReason);
