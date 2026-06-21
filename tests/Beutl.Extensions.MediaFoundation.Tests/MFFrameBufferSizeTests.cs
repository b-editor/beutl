using System.IO;

using Beutl.Embedding.MediaFoundation.Decoding;

namespace Beutl.Extensions.MediaFoundation.Tests;

[TestFixture]
public class MFFrameBufferSizeTests
{
    [Test]
    public void CalculateYuy2_WithValidDimensions_ReturnsByteCount()
    {
        int size = MFFrameBufferSize.CalculateYuy2(1920, 1080);

        Assert.That(size, Is.EqualTo(1920 * 1080 * 2));
    }

    [TestCase(0, 1080, 2)]
    [TestCase(1920, 0, 2)]
    [TestCase(1920, 1080, 0)]
    [TestCase(-1, 1080, 2)]
    [TestCase(1920, -1, 2)]
    [TestCase(1920, 1080, -1)]
    public void Calculate_WithNonPositiveInput_ThrowsInvalidDataException(
        int width,
        int height,
        int bytesPerPixel)
    {
        Assert.That(
            () => MFFrameBufferSize.Calculate(width, height, bytesPerPixel),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void Calculate_WhenByteCountExceedsMaximumArrayLength_ThrowsInvalidDataException()
    {
        Assert.That(
            () => MFFrameBufferSize.Calculate(int.MaxValue, int.MaxValue, 2),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void Calculate_WhenByteCountOverflowsInt64_ThrowsInvalidDataException()
    {
        Assert.That(
            () => MFFrameBufferSize.Calculate(int.MaxValue, int.MaxValue, int.MaxValue),
            Throws.TypeOf<InvalidDataException>());
    }
}
