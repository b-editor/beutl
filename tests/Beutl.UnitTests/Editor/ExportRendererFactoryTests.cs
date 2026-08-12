using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Models;
using Beutl.ProjectSystem;

namespace Beutl.UnitTests.Editor;

public sealed class ExportRendererFactoryTests
{
    [Test]
    public void Create_ConfiguresTheRendererForDeliveryGradeOutput()
    {
        var scene = new Scene(240, 120, "Export");

        using SceneRenderer renderer = ExportRendererFactory.Create(scene, renderScale: 2f);

        Assert.Multiple(() =>
        {
            Assert.That(renderer.Intent, Is.EqualTo(RenderIntent.Delivery),
                "An export must fail on an intermediate allocation failure, not silently drop content.");
            Assert.That(renderer.MaxWorkingScale, Is.EqualTo(WorkingScaleCeiling.Export()));
            Assert.That(renderer.OutputScale, Is.EqualTo(2f));
            Assert.That(renderer.CacheOptions, Is.SameAs(RenderCacheOptions.Disabled));
            Assert.That(renderer.Compositor.ForceOriginalSource, Is.True);
            Assert.That(renderer.Compositor.DisableResourceShare, Is.True);
        });
    }

    [Test]
    public void PreviewSceneRenderer_StaysOnPreviewIntent()
    {
        var scene = new Scene(240, 120, "Preview");

        using var renderer = new SceneRenderer(scene, maxWorkingScale: WorkingScaleCeiling.Preview(1f));

        Assert.That(renderer.Intent, Is.EqualTo(RenderIntent.Preview));
    }
}
