using Beutl.Extensions.AVFoundation.Decoding;
using Beutl.Media.Decoding;

namespace Beutl.Extensions.AVFoundation.Tests;

// The MediaReader contract for AVFoundation readers.
[TestFixture]
[Platform("MacOSX")]
public sealed class AVFMediaReaderConformanceTests : MediaReaderConformanceTests
{
    protected override IReadOnlyDictionary<string, string> KnownDeviations { get; } = new Dictionary<string, string>
    {
        [nameof(ReadVideo_AfterAReadPastTheEnd_ReadsEarlierFrames)] =
            "#3244, a seek past the end leaves the reader failing frames it can decode",
    };

    protected override MediaReader? Open(string path, MediaOptions options)
        => new AVFDecoderInfo(new AVFDecodingExtension()).Open(path, options);
}
