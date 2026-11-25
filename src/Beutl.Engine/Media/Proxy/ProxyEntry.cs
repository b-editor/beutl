using System.Text.Json.Serialization;

using Beutl.Media;
using Beutl.Media.Decoding;

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

    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonConverter(typeof(ProxyColorSpaceJsonConverter))]
    internal BitmapColorSpace? OriginalColorSpace { get; init; }


    public Rational? OriginalDuration { get; init; }
}
