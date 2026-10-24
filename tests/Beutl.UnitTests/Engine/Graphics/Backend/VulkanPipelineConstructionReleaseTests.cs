using System.Text.RegularExpressions;

namespace Beutl.UnitTests.Engine.Graphics.Backend;

[TestFixture]
public sealed class VulkanPipelineConstructionReleaseTests
{
    [Test]
    public void VulkanShaderCompiler_RetainsOneNativeApi()
    {
        DirectoryInfo directory = FindRepositoryRoot();
        string path = Path.Combine(
            directory.FullName,
            "src", "Beutl.Engine", "Graphics", "Backend", "Vulkan", "VulkanShaderCompiler.cs");
        string source = File.ReadAllText(path);

        Assert.Multiple(() =>
        {
            Assert.That(Regex.Matches(source, @"Shaderc\.GetApi\s*\(\s*\)"), Has.Count.EqualTo(1));
            Assert.That(source, Does.Not.Contain("s_shaderc.Dispose()"));
        });
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Beutl.slnx")))
            directory = directory.Parent;

        Assert.That(directory, Is.Not.Null, "the repository root was not found above the test binaries");
        return directory!;
    }
}
