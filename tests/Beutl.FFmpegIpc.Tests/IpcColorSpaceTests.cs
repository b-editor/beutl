using Beutl.FFmpegIpc.Protocol;
using Beutl.Media;

namespace Beutl.FFmpegIpc.Tests;

[TestFixture]
public class IpcColorSpaceTests
{
    // Render frames are sRGB or linear sRGB, and read back as the same shared instances they were before frames carried
    // a color space.
    [Test]
    public void The_render_color_spaces_come_back_as_their_shared_instances()
    {
        (float[] srgbFn, float[] srgbXyz) = IpcColorSpace.ToArrays(BitmapColorSpace.Srgb);
        (float[] linearFn, float[] linearXyz) = IpcColorSpace.ToArrays(BitmapColorSpace.LinearSrgb);

        Assert.Multiple(() =>
        {
            Assert.That(IpcColorSpace.FromArrays(srgbFn, srgbXyz), Is.SameAs(BitmapColorSpace.Srgb));
            Assert.That(IpcColorSpace.FromArrays(linearFn, linearXyz), Is.SameAs(BitmapColorSpace.LinearSrgb));
        });
    }

    [TestCase(6, 9)]
    [TestCase(7, 8)]
    public void A_color_space_of_the_wrong_size_is_refused(int transferFnLength, int toXyzD50Length)
    {
        Assert.Throws<InvalidOperationException>(
            () => IpcColorSpace.FromArrays(new float[transferFnLength], new float[toXyzD50Length]));
    }
}
