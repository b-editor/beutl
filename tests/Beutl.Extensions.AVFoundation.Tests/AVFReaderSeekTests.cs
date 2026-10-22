using System.Runtime.InteropServices;
using Beutl.Extensions.AVFoundation.Decoding;
using Beutl.Media.Decoding;

namespace Beutl.Extensions.AVFoundation.Tests;

// A read past the end seeks without decoding a frame, and the reads after it must still decode earlier frames.
[TestFixture]
[Platform("MacOSX")]
public class AVFReaderSeekTests
{
    // 40 frames at 10 fps, red before 1 s and blue from then on.
    private static string FixturePath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "marker-bframes.mp4");

    [TestCase(new[] { 50, 5, 6, 35, 5 })]
    [TestCase(new[] { 10, 80, 20, 21, 22, 60, 20 })]
    public void ReadVideo_AfterAReadPastTheEnd_ReadsEarlierFrames(int[] frames)
    {
        using var reader = new AVFReader(FixturePath, new MediaOptions(MediaMode.Video), new AVFDecodingExtension());

        Assert.Multiple(() =>
        {
            foreach (int frame in frames)
            {
                bool read = reader.ReadVideo(frame, out var image);
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
