using Beutl.Editor.VersionControl;

namespace Beutl.UnitTests.Editor.VersionControl;

[TestFixture]
public class GitProcessEnvironmentTests
{
    [TestCase("/usr/bin:/bin", "/usr/bin:/bin:/opt/homebrew/bin:/usr/local/bin")]
    [TestCase("/custom/bin:/usr/local/bin:/usr/bin", "/custom/bin:/usr/local/bin:/usr/bin:/opt/homebrew/bin")]
    [TestCase("/opt/homebrew/bin:/usr/bin", "/opt/homebrew/bin:/usr/bin:/usr/local/bin")]
    [TestCase("/opt/homebrew/bin:/usr/local/bin:/opt/homebrew/bin", "/opt/homebrew/bin:/usr/local/bin:/opt/homebrew/bin")]
    [TestCase(":tools:/bin:", ":tools:/bin::/opt/homebrew/bin:/usr/local/bin")]
    [TestCase("", ":/opt/homebrew/bin:/usr/local/bin")]
    [TestCase(null, "/usr/bin:/bin:/usr/sbin:/sbin:/opt/homebrew/bin:/usr/local/bin")]
    public void Mac_tool_search_path_adds_missing_homebrew_directories_without_reordering_existing_entries(
        string? path,
        string expected)
    {
        Assert.That(GitProcessEnvironment.GetMacToolSearchPath(path), Is.EqualTo(expected));
    }
}
