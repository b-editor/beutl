using Beutl.Extensions.FFmpeg.Decoding;
using Beutl.Media.Decoding;

namespace Beutl.FFmpegIpc.Tests;

// The MediaReader contract for FFmpeg readers, which decode in the shared worker process.
[TestFixture, NonParallelizable]
public sealed class FFmpegMediaReaderConformanceTests : MediaReaderConformanceTests
{
    [SetUp]
    public void SetUp()
    {
        DecodingWorkerTestHelpers.RequireDecodingWorker();
    }

    protected override MediaReader? Open(string path, MediaOptions options)
        => new FFmpegDecoderInfo(new FFmpegDecodingSettings()).Open(path, options);
}
