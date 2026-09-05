using System.Text.RegularExpressions;

namespace Beutl.UnitTests.Engine.Graphics.Backend;

[TestFixture]
public sealed class VulkanImageTeardownOrderTests
{
    private static readonly Regex s_freeBeforeDestroy = new(
        @"vk\.FreeMemory\([^;]*\);\s*\r?\n\s*vk\.DestroyImage\(",
        RegexOptions.Compiled);

    [Test]
    public void EveryVulkanTeardown_DestroysTheImageBeforeFreeingItsMemory()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Beutl.slnx")))
            directory = directory.Parent;

        Assert.That(directory, Is.Not.Null, "the repository root was not found above the test binaries");

        string backend = Path.Combine(
            directory!.FullName, "src", "Beutl.Engine", "Graphics", "Backend", "Vulkan");
        Assert.That(Directory.Exists(backend), Is.True, $"the Vulkan backend was not found at {backend}");

        var offenders = new List<string>();
        int scanned = 0;
        foreach (string path in Directory.EnumerateFiles(backend, "*.cs", SearchOption.AllDirectories))
        {
            scanned++;
            string source = File.ReadAllText(path);
            foreach (Match match in s_freeBeforeDestroy.Matches(source))
            {
                int line = source.Take(match.Index).Count(character => character == '\n') + 1;
                offenders.Add($"{Path.GetFileName(path)}:{line}");
            }
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(scanned, Is.GreaterThan(0), "the scan must actually read the backend sources");
            Assert.That(
                offenders,
                Is.Empty,
                "a bound image must be destroyed before its memory is freed: "
                + string.Join(", ", offenders));
        }
    }
}
