using System.Runtime.Versioning;

using Beutl.Embedding.MediaFoundation.Decoding;
using Beutl.Media.Decoding;

namespace Beutl.Extensions.MediaFoundation.Tests;

// The MediaReader contract for Media Foundation readers.
[TestFixture]
[Platform("Win")]
[SupportedOSPlatform("windows")]
public sealed class MFMediaReaderConformanceTests : MediaReaderConformanceTests
{
    protected override IReadOnlyDictionary<string, string> KnownDeviations { get; } = new Dictionary<string, string>
    {
        [nameof(ReadVideo_PastTheLastFrame_ReturnsFalse)] = "#3249, a read past the last frame throws",
        [nameof(ReadVideo_AfterAReadPastTheEnd_ReadsEarlierFrames)] = "#3249, a read past the last frame throws",
    };

    protected override MediaReader? Open(string path, MediaOptions options)
        => new MFDecoderInfo(new MFDecodingExtension()).Open(path, options);
}
