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
    protected override MediaReader? Open(string path, MediaOptions options)
        => new MFDecoderInfo(new MFDecodingExtension()).Open(path, options);
}
