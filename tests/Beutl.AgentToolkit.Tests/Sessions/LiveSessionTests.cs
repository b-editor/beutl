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
            // One dispatch for the CurrentSession liveness probe (RequireSession) plus one for the apply.
            Assert.That(binding.InvokeCount, Is.EqualTo(2));
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

        ToolError error = ex!.ToError();
        Assert.Multiple(() =>
        {
            Assert.That(error.Code, Is.EqualTo(ErrorCode.NoActiveEditorSession));
            Assert.That(error.Hint, Does.Contain("attach_active_editor"));
            Assert.That(error.Hint, Does.Contain("open_project"));
        });
    }

    [Test]
    public void Current_session_probes_liveness_through_binding_dispatcher()
    {
        var scene = new Scene(1920, 1080, "Scene");
        using RecordingPipeline recording = RecordingPipeline.Create(scene);
        var binding = new MutableFakeLiveBinding(scene, recording.History);
        var source = new LiveSessionSource();
        source.Attach(binding);

        Assert.That(source.CurrentSession, Is.Not.Null);

        binding.Alive = false;

        Assert.That(source.CurrentSession, Is.Null, "A dead binding must surface as no current session.");
    }

    [Test]
    public void Invoke_checks_liveness_inside_binding_dispatcher()
    {
        var scene = new Scene(1920, 1080, "Scene");
        using RecordingPipeline recording = RecordingPipeline.Create(scene);
        var binding = new GuardedFakeLiveBinding(scene, recording.History);
        var source = new LiveSessionSource();
        LiveEditingSession session = source.Attach(binding);
        binding.RequireDispatchedAccess = true;

        Assert.DoesNotThrow(() => session.Invoke(() => { }));
    }

    [Test]
    public void Current_session_key_reads_live_root_through_binding_dispatcher()
    {
        var scene = new Scene(1920, 1080, "Scene");
        using RecordingPipeline recording = RecordingPipeline.Create(scene);
        var binding = new FakeLiveBinding(scene, recording.History);
        var source = new LiveSessionSource();
        source.Attach(binding);
        var manager = new AgentSessionManager();
        manager.UseSource(source);

        string key = manager.CurrentSessionKey;

        Assert.Multiple(() =>
        {
            Assert.That(key, Is.EqualTo($"{EditingSessionSource.LiveEditor}:{scene.Id}"));
            Assert.That(binding.InvokeCount, Is.EqualTo(2));
        });
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

    private sealed class MutableFakeLiveBinding : ILiveSessionBinding
    {
        public MutableFakeLiveBinding(Scene? scene, HistoryManager? history)
        {
            ActiveScene = scene;
            ActiveHistory = history;
        }

        public bool Alive { get; set; } = true;

        public Scene? ActiveScene { get; private set; }

        public HistoryManager? ActiveHistory { get; private set; }

        public bool IsAlive => Alive && ActiveScene is not null && ActiveHistory is not null;

        public void Invoke(Action action) => action();
    }

    private sealed class GuardedFakeLiveBinding(Scene? scene, HistoryManager? history) : ILiveSessionBinding
    {
        private bool _insideInvoke;

        public bool RequireDispatchedAccess { get; set; }

        public Scene? ActiveScene
        {
            get
            {
                EnsureDispatched();
                return scene;
            }
        }

        public HistoryManager? ActiveHistory
        {
            get
            {
                EnsureDispatched();
                return history;
            }
        }

        public bool IsAlive
        {
            get
            {
                EnsureDispatched();
                return scene is not null && history is not null;
            }
        }

        public void Invoke(Action action)
        {
            _insideInvoke = true;
            try
            {
                action();
            }
            finally
            {
                _insideInvoke = false;
            }
        }

        private void EnsureDispatched()
        {
            if (RequireDispatchedAccess && !_insideInvoke)
            {
                throw new InvalidOperationException("Live binding was read outside the dispatcher.");
            }
        }
    }
}
