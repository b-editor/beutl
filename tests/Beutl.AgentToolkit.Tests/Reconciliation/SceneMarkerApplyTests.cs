using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tools;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.AgentToolkit.Tests.Reconciliation;

// Scene.Markers is [NotAutoSerialized] but custom-serialized, so it must flow through the
// applier's explicit handler rather than the generic registered-property pass.
[TestFixture]
public sealed class SceneMarkerApplyTests
{
    [Test]
    public void Apply_edit_patch_updates_scene_markers()
    {
        using var source = new FileSessionSource();
        FileEditingSession session = CreateSession(source);
        var manager = new AgentSessionManager();
        manager.UseSource(source);
        var tools = new EditTools(manager);

        JsonObject markerJson = CoreSerializer.SerializeToJsonObject(
            new SceneMarker(TimeSpan.FromSeconds(2), "beat", note: "drop"));
        markerJson.Remove(nameof(CoreObject.Id));
        JsonObject patch = new()
        {
            [nameof(Scene.Markers)] = new JsonArray(markerJson)
        };

        ToolResult<ApplyEditResponse> apply = tools.ApplyEdit(patch: patch, schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(apply.IsSuccess, Is.True, apply.Error?.Message);
            Assert.That(session.Scene.Markers, Has.Count.EqualTo(1));
            Assert.That(session.Scene.Markers[0].Time, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(session.Scene.Markers[0].Name, Is.EqualTo("beat"));
            Assert.That(session.Scene.Markers[0].Note, Is.EqualTo("drop"));
        });
    }

    [Test]
    public void Full_document_omitting_markers_clears_them()
    {
        using var source = new FileSessionSource();
        FileEditingSession session = CreateSession(source);
        session.Scene.Markers.Add(new SceneMarker(TimeSpan.FromSeconds(1), "stale"));

        JsonObject desired = session.Documents.Read(session.Scene);
        desired.Remove(nameof(Scene.Markers));

        session.Documents.Write(session.Scene, desired);

        Assert.That(session.Scene.Markers, Is.Empty);
    }

    [Test]
    public void Document_roundtrip_preserves_markers()
    {
        using var source = new FileSessionSource();
        FileEditingSession session = CreateSession(source);
        session.Scene.Markers.Add(new SceneMarker(TimeSpan.FromSeconds(3), "keep"));

        JsonObject desired = session.Documents.Read(session.Scene);
        session.Documents.Write(session.Scene, desired);

        Assert.Multiple(() =>
        {
            Assert.That(session.Scene.Markers, Has.Count.EqualTo(1));
            Assert.That(session.Scene.Markers[0].Time, Is.EqualTo(TimeSpan.FromSeconds(3)));
            Assert.That(session.Scene.Markers[0].Name, Is.EqualTo("keep"));
        });
    }

    private static FileEditingSession CreateSession(FileSessionSource source)
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return source.CreateProject(new ProjectCreateOptions(
            Path.Combine(root, "demo.bep"), 640, 360, 30, TimeSpan.FromSeconds(6)));
    }
}
