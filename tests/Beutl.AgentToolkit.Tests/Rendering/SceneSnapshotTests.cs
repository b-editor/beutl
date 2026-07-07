using Beutl.AgentToolkit.Rendering;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.AgentToolkit.Tools;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Tests.Rendering;

public sealed class SceneSnapshotTests
{
    [Test]
    public void File_session_returns_the_live_scene_by_default()
    {
        var scene = new Scene(640, 360, "Scene");
        using var session = new AgentToolkitTestSession(scene, EditingSessionSource.File);

        Scene snapshot = RenderTools.CreateSceneSnapshot(session, forceClone: false);

        Assert.That(snapshot, Is.SameAs(scene));
    }

    [Test]
    public void Force_clone_snapshots_a_file_session_scene_for_background_renders()
    {
        var scene = new Scene(640, 360, "Scene");
        using var session = new AgentToolkitTestSession(scene, EditingSessionSource.File);

        Scene snapshot = RenderTools.CreateSceneSnapshot(session, forceClone: true);

        Assert.That(snapshot, Is.Not.SameAs(scene));
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
