using Beutl.Editor.Components.FileBrowserTab.Services;

namespace Beutl.UnitTests.Editor.FileBrowser;

[TestFixture]
public class MediaFileInfoTests
{
    [Test]
    public void FormatFileSize_shows_terabytes_like_the_proxy_sizes()
    {
        Assert.That(MediaFileInfo.FormatFileSize(3L << 40), Is.EqualTo("3 TB"));
    }
}
