using System.Runtime.InteropServices;

namespace Beutl.UnitTests.Core;

[TestFixture]
public class FilePathComparisonTests
{
    // Two paths differing only in case are the same file on Windows and macOS and two different
    // files on Linux, so a cache keyed on a path must not fold them together there.
    [Test]
    public void Equals_FollowsThePlatformsCaseRules()
    {
        bool expected = !RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

        Assert.Multiple(() =>
        {
            Assert.That(FilePathComparison.Equals("/tmp/pkg/Item.json", "/tmp/pkg/item.json"), Is.EqualTo(expected));
            Assert.That(FilePathComparison.StartsWith("/tmp/Templates/a.json", "/tmp/templates/"), Is.EqualTo(expected));
        });
    }

    [Test]
    public void Equals_IsExactRegardlessOfPlatform()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FilePathComparison.Equals("/tmp/pkg/item.json", "/tmp/pkg/item.json"), Is.True);
            Assert.That(FilePathComparison.Equals("/tmp/pkg/item.json", "/tmp/pkg/other.json"), Is.False);
            Assert.That(FilePathComparison.Equals(null, null), Is.True);
            Assert.That(FilePathComparison.StartsWith("/tmp/templates/a.json", "/tmp/templates/"), Is.True);
            Assert.That(FilePathComparison.StartsWith("/tmp/other/a.json", "/tmp/templates/"), Is.False);
        });
    }
}
