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
}
