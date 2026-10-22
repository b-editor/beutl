using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using Beutl.Embedding.MediaFoundation.Decoding;
using Beutl.Media.Decoding;

namespace Beutl.Extensions.MediaFoundation.Tests;

// A read past the last frame returns false, and the reads after it still decode their own frames.
[TestFixture]
[Platform("Win")]
[SupportedOSPlatform("windows")]
public class MFReaderPastTheEndTests
{
    // marker-bframes.mp4 has 40 frames at 10 fps, red before 1 s and blue from then on.
    [TestCase(new[] { 50, 5, 6, 35, 5 })]
    [TestCase(new[] { 10, 80, 20, 21, 22, 60, 20 })]
    [TestCase(new[] { 9, 80, 9 })]
    [TestCase(new[] { 2, 40, 0 })]
    public void ReadVideo_PastTheEnd_ReturnsFalse_AndLaterReadsDecodeTheirFrames(int[] frames)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "marker-bframes.mp4");
        using MediaReader? reader = new MFDecoderInfo(new MFDecodingExtension()).Open(path, new MediaOptions(MediaMode.Video));
        Assert.That(reader, Is.TypeOf<MFReader>());

        Assert.Multiple(() =>
        {
            foreach (int frame in frames)
            {
                bool read = reader!.ReadVideo(frame, out var image);
                using (image)
                {
                    Assert.That(read, Is.EqualTo(frame < 40), $"frame {frame}");
                    if (read)
                    {
                        nint pixel = image!.Value.Data + image.Value.RowBytes * 32 + 32 * 4;
                        int dominantChannel = frame < 10 ? 2 : 0; // BGRA: red, then blue.
                        Assert.That(Marshal.ReadByte(pixel, dominantChannel), Is.GreaterThan(240), $"frame {frame}");
                    }
                }
            }
        });
    }
}
