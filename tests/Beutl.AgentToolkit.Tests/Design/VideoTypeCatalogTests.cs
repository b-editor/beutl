using Beutl.AgentToolkit.Design;
using Beutl.AgentToolkit.Reconciliation;

namespace Beutl.AgentToolkit.Tests.Design;

public sealed class VideoTypeCatalogTests
{
    [TestCase("motion-graphics")]
    [TestCase("footage-cut")]
    [TestCase("slideshow")]
    [TestCase("lyric-captions")]
    [TestCase("logo-intro")]
    public void Resolve_accepts_supported_names_case_insensitively(string name)
    {
        VideoTypeProfile profile = VideoTypeCatalog.Resolve(name.ToUpperInvariant());

        Assert.That(profile.Name, Is.EqualTo(name));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Resolve_defaults_empty_values_to_motion_graphics(string? name)
    {
        VideoTypeProfile profile = VideoTypeCatalog.Resolve(name);

        Assert.That(profile.Name, Is.EqualTo("motion-graphics"));
    }

    [Test]
    public void Resolve_rejects_unknown_values_with_supported_names()
    {
        ReconcileException ex = Assert.Throws<ReconcileException>(() => VideoTypeCatalog.Resolve("tutorial"))!;

        Assert.Multiple(() =>
        {
            Assert.That(ex.Error.Code, Is.EqualTo("validation_rejected"));
            Assert.That(ex.Error.Target, Is.EqualTo("videoType"));
            foreach (string name in VideoTypeCatalog.SupportedNames)
            {
                Assert.That(ex.Error.Message, Does.Contain(name));
            }
        });
    }

    [TestCase("footage-cut")]
    [TestCase("slideshow")]
    [TestCase("lyric-captions")]
    public void Media_dependent_workflow_steps_reference_asset_sourcing(string name)
    {
        VideoTypeProfile profile = VideoTypeCatalog.Resolve(name);

        Assert.That(profile.WorkflowSteps, Has.Some.Contains("beutl-agent-asset-sourcing"));
    }
}
