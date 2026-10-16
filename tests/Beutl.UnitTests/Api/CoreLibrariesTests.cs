using Beutl.Api.Services;
using NuGet.Versioning;

namespace Beutl.UnitTests.Api;

[TestFixture]
public sealed class CoreLibrariesTests
{
    [TestCase("Beutl.Engine")]
    [TestCase("Beutl.Engine.SourceGenerators")]
    [TestCase("beutl.controls")]
    [TestCase("Beutl.Embedding.MediaFoundation")]
    [TestCase("Beutl.AgentToolkit")]
    [TestCase("Beutl.FFmpegIpc")]
    [TestCase("Beutl.Extensions.FFmpeg.Core")]
    public void A_Beutl_library_comes_from_the_host_at_any_version_and_is_never_downloaded(string name)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(CoreLibraries.IsBeutlLibrary(name), Is.True);
            Assert.That(CoreLibraries.IncludedInPackageDependencies(name, new NuGetVersion(99, 0, 0)), Is.True);
            Assert.That(CoreLibraries.IncludedInPackageDependencies(name, VersionRange.Parse("[99.0.0, )")), Is.True);
        }
    }

    [TestCase("Beutl.Extensions.Danmaku")]
    [TestCase("Beutl.FFmpegWorker")]
    [TestCase("Avalonia")]
    public void Extensions_and_other_libraries_are_not_Beutl_libraries(string name)
    {
        Assert.That(CoreLibraries.IsBeutlLibrary(name), Is.False);
    }
}
