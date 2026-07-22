using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
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

    // With existing markers the identity merge silently drops non-object patch entries, so the
    // null only reaches the applier when the array is replaced wholesale (no current markers).
    [Test]
    public void Null_marker_entry_is_rejected_without_mutation()
    {
        using var source = new FileSessionSource();
        FileEditingSession session = CreateSession(source);
        var manager = new AgentSessionManager();
        manager.UseSource(source);
        var tools = new EditTools(manager);

        JsonObject patch = new()
        {
            [nameof(Scene.Markers)] = new JsonArray((JsonNode?)null)
        };

        ToolResult<ApplyEditResponse> apply = tools.ApplyEdit(patch: patch, schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(apply.IsSuccess, Is.False);
            Assert.That(apply.Error!.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(session.Scene.Markers, Is.Empty);
        });
    }

    [Test]
    public void Explicit_null_markers_member_is_rejected_without_mutation()
    {
        using var source = new FileSessionSource();
        FileEditingSession session = CreateSession(source);
        session.Scene.Markers.Add(new SceneMarker(TimeSpan.FromSeconds(1), "keep"));

        JsonObject desired = session.Documents.Read(session.Scene);
        desired[nameof(Scene.Markers)] = null;

        Assert.Multiple(() =>
        {
            Assert.That(
                () => session.Documents.Write(session.Scene, desired),
                Throws.InstanceOf<ReconcileException>());
            Assert.That(session.Scene.Markers, Has.Count.EqualTo(1));
            Assert.That(session.Scene.Markers[0].Name, Is.EqualTo("keep"));
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
