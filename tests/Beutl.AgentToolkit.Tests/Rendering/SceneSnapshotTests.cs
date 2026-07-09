using Beutl.AgentToolkit.Rendering;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.AgentToolkit.Tools;
using Beutl.Graphics3D;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Tests.Rendering;

public sealed class SceneSnapshotTests
{
    [Test]
    public void File_session_snapshots_are_isolated_clones()
    {
        var scene = new Scene(640, 360, "Scene");
        using var session = new AgentToolkitTestSession(scene, EditingSessionSource.File);

        // Renders run after ReadOnSession releases the dispatch lock, so a concurrent apply_edit
        // could mutate a shared live scene mid-render; every snapshot must be a clone.
        Scene snapshot = RenderTools.CreateSceneSnapshot(session);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot, Is.Not.SameAs(scene));
            Assert.That(snapshot.Id, Is.EqualTo(scene.Id));
        });
    }

    [Test]
    public void Gpu_preflight_scopes_to_the_sampled_time_and_visible_window()
    {
        string dir = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var scene = new Scene(640, 360, "Scene")
        {
            Duration = TimeSpan.FromSeconds(4),
            Uri = new Uri(Path.Combine(dir, "Scene.scene"))
        };
        var element = new Element
        {
            Start = TimeSpan.Zero,
            Length = TimeSpan.FromSeconds(2),
            Uri = new Uri(Path.Combine(dir, "el.belm"))
        };
        element.AddObject(new Scene3D());
        scene.Children.Add(element);

        Assert.Multiple(() =>
        {
            Assert.That(StillRenderer.ContainsGpuOnlyContent(scene, TimeSpan.FromSeconds(1)), Is.True);
            Assert.That(StillRenderer.ContainsGpuOnlyContent(scene, TimeSpan.FromSeconds(3)), Is.False);
            Assert.That(StillRenderer.ContainsGpuOnlyContent(scene), Is.True);
        });

        // Trimming the scene to [10s, 14s) puts the 3D element outside the visible window entirely.
        scene.Start = TimeSpan.FromSeconds(10);

        Assert.Multiple(() =>
        {
            Assert.That(StillRenderer.ContainsGpuOnlyContent(scene, TimeSpan.FromSeconds(1)), Is.False);
            Assert.That(StillRenderer.ContainsGpuOnlyContent(scene), Is.False);
        });
    }

    [Test]
    public void Active_element_summary_filters_at_the_scene_start_offset()
    {
        string dir = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var scene = new Scene(640, 360, "Scene")
        {
            Start = TimeSpan.FromSeconds(10),
            Uri = new Uri(Path.Combine(dir, "Scene.scene"))
        };
        var element = new Element
        {
            Start = TimeSpan.FromSeconds(10),
            Length = TimeSpan.FromSeconds(2),
            Uri = new Uri(Path.Combine(dir, "el.belm"))
        };
        scene.Children.Add(element);

        // The frame at local time 1s renders at 11s (time + scene.Start), where the element is visible.
        IReadOnlyList<RenderStillActiveElement> visible = StillRenderer.CreateActiveElementSummaries(scene, TimeSpan.FromSeconds(1));
        // Local time 5s renders at 15s, past the element's range.
        IReadOnlyList<RenderStillActiveElement> hidden = StillRenderer.CreateActiveElementSummaries(scene, TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(visible.Select(e => e.Id), Does.Contain(element.Id.ToString()));
            Assert.That(hidden, Is.Empty);
        });
    }
}
