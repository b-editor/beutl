using Beutl.Configuration;

namespace Beutl.Media.Source.Proxy;

public sealed record ProxyEntry(
    string OriginalPath,
    long OriginalSize,
    DateTime OriginalMtime,
    PixelSize OriginalFrameSize,
    string ProxyPath,
    long ProxyFileSize,
    PixelSize ProxyFrameSize,
    ProxyPresetKind Preset,
    DateTime GeneratedAt,
    int SchemaVersion);
