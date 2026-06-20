using Beutl.Embedding.MediaFoundation.Decoding;

using Vortice.MediaFoundation;

namespace Beutl.Extensions.MediaFoundation.Tests;

[TestFixture]
public class VideoFormatNameTests
{
    [Test]
    public void GetName_KnownFormatGuid_ReturnsFieldName()
        => Assert.That(VideoFormatName.GetName(VideoFormatGuids.YUY2), Is.EqualTo(nameof(VideoFormatGuids.YUY2)));

    [Test]
    public void GetName_UnknownGuid_ReturnsNull()
        => Assert.That(VideoFormatName.GetName(Guid.Empty), Is.Null);
}
