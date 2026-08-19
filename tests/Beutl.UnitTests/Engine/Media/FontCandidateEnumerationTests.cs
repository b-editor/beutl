using System.Runtime.Versioning;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Media;

[TestFixture]
public class FontCandidateEnumerationTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), $"beutl-fonts-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_root, "pkg"));
    }

    [TearDown]
    public void TearDown()
    {
        if (!Directory.Exists(_root)) return;

        if (!OperatingSystem.IsWindows())
        {
            foreach (string dir in Directory.EnumerateDirectories(_root, "*", SearchOption.AllDirectories))
            {
                File.SetUnixFileMode(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }

        Directory.Delete(_root, recursive: true);
    }

    [Test]
    public void KeepsOnlyFontExtensions()
    {
        File.WriteAllText(Path.Combine(_root, "pkg", "a.ttf"), "");
        File.WriteAllText(Path.Combine(_root, "pkg", "b.OTF"), "");
        File.WriteAllText(Path.Combine(_root, "pkg", "c.png"), "");

        string[] found = [.. FontManager.EnumerateFontCandidates(_root).Select(Path.GetFileName)!];

        Assert.That(found, Is.EquivalentTo(new[] { "a.ttf", "b.OTF" }));
    }

    // FontManager builds its map during static initialization, so a directory it cannot
    // read has to be skipped rather than take the whole process down at startup.
    [Test]
    [UnsupportedOSPlatform("windows")]
    public void SkipsAnUnreadableSubdirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Clearing the read permission is a Unix-only way to make a directory unreadable.");
        }

        File.WriteAllText(Path.Combine(_root, "pkg", "a.ttf"), "");
        string blocked = Path.Combine(_root, "blocked");
        Directory.CreateDirectory(blocked);
        File.WriteAllText(Path.Combine(blocked, "b.ttf"), "");
        File.SetUnixFileMode(blocked, UnixFileMode.None);

        string[] found = [.. FontManager.EnumerateFontCandidates(_root).Select(Path.GetFileName)!];

        Assert.That(found, Does.Contain("a.ttf"));
    }
}
