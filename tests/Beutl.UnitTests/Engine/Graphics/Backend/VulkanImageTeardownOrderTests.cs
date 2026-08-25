using System.Text.RegularExpressions;

namespace Beutl.UnitTests.Engine.Graphics.Backend;

/// <summary>
/// Pins that every Vulkan teardown destroys an image before freeing the memory it is bound to.
/// </summary>
/// <remarks>
/// The order is a Vulkan requirement, not a preference: freeing memory that still has an image bound to it
/// is invalid usage, so the validation layer reports it and a driver is free to do anything. The paths that
/// get it wrong are the ones that run when construction fails partway, which no test can reach without
/// making a real allocation fail - so the ordering is pinned at the source instead, across every site at
/// once rather than the one an exercisable path happens to cover.
/// </remarks>
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
