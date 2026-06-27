using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tools;
using Beutl.Editor;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Tests.Sessions;

public sealed class LiveSessionTests
{
    [Test]
    public void Live_session_applies_on_binding_writer_and_undo_reverts()
    {
        string dir = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var scene = new Scene(1920, 1080, "Scene")
        {
            Uri = new Uri(Path.Combine(dir, "Scene.scene"))
        };
        using RecordingPipeline recording = RecordingPipeline.Create(scene);
        var binding = new FakeLiveBinding(scene, recording.History);
        var source = new LiveSessionSource();
        LiveEditingSession session = source.Attach(binding);
        var manager = new AgentSessionManager();
        manager.UseSource(source);
        var tools = new EditTools(manager);

        JsonObject patch = new()
        {
            [nameof(Scene.Duration)] = TimeSpan.FromSeconds(8).ToString("c")
        };

        var result = tools.ApplyEdit(patch: patch, schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(binding.InvokeCount, Is.EqualTo(1));
            Assert.That(scene.Duration, Is.EqualTo(TimeSpan.FromSeconds(8)));
        });

        session.History.Undo();
        Assert.That(scene.Duration, Is.EqualTo(TimeSpan.FromMinutes(5)));
    }

    [Test]
    public void Missing_live_binding_reports_no_active_editor_session()
    {
        var source = new LiveSessionSource();
        var ex = Assert.Throws<SessionUnavailableException>(() => source.Attach(new FakeLiveBinding(null, null)));

        Assert.That(ex!.ToError().Code, Is.EqualTo(ErrorCode.NoActiveEditorSession));
    }

    private sealed class FakeLiveBinding(Scene? scene, HistoryManager? history) : ILiveSessionBinding
    {
        public int InvokeCount { get; private set; }

        public Scene? ActiveScene { get; } = scene;

        public HistoryManager? ActiveHistory { get; } = history;

        public bool IsAlive => ActiveScene is not null && ActiveHistory is not null;

        public void Invoke(Action action)
        {
            InvokeCount++;
            action();
        }
    }
}
