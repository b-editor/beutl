using Beutl.Media.Decoding;
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
    string? FailureReason)
{
    public VideoDeinterlaceOptions Deinterlace { get; init => field = value.Normalize(); }

    public VideoFieldOrder DetectedFieldOrder { get; init; }

    public DeinterlaceApplicationStatus DeinterlaceStatus { get; init; }

    public VideoColorTags OriginalColorTags { get; init; }

    public Rational? OriginalDuration { get; init; }
}
