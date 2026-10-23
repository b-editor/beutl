using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.AgentToolkit.Tools;
using Beutl.Editor;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Tests.Sessions;

public sealed class LiveSessionTests
{
    [Test]
    public void Live_session_applies_on_binding_writer_and_undo_reverts()
    {
        Scene scene = TestWorkspace.CreateScene();
        using RecordingPipeline recording = RecordingPipeline.Create(scene);
        var binding = new FakeLiveBinding(scene, recording.History);
        LiveEditingSession session = LiveEditingSession.Create(binding);
        var manager = new AgentSessionManager();
        manager.UseRequestTarget(() => session);
        var tools = new EditTools(manager);

        JsonObject patch = new()
        {
            [nameof(Scene.Duration)] = TimeSpan.FromSeconds(8).ToString("c")
        };

        var result = tools.ApplyEdit(patch: patch, schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            // One dispatch for the apply.
            Assert.That(binding.InvokeCount, Is.EqualTo(1));
            Assert.That(scene.Duration.Value, Is.EqualTo(MediaTime.FromSeconds(8)));
        });

        session.History.Undo();
        Assert.That(scene.Duration.Value, Is.EqualTo(MediaTime.FromSeconds(300)));
    }

    [Test]
    public void Missing_live_binding_reports_no_active_editor_session()
    {
        var ex = Assert.Throws<SessionUnavailableException>(() => LiveEditingSession.Create(new FakeLiveBinding(null, null)));

        ToolError error = ex!.ToError();
        Assert.Multiple(() =>
        {
            Assert.That(error.Code, Is.EqualTo(ErrorCode.NoActiveEditorSession));
            Assert.That(error.Hint, Does.Contain("sceneId"));
            Assert.That(error.Hint, Does.Contain("open_project"));
        });
    }

    [Test]
    public void Probe_reports_liveness_through_binding_dispatcher()
    {
        var scene = new Scene(1920, 1080, "Scene");
        using RecordingPipeline recording = RecordingPipeline.Create(scene);
        var binding = new FakeLiveBinding(scene, recording.History);
        LiveEditingSession session = LiveEditingSession.Create(binding);

        Assert.That(session.ProbeIsAlive(), Is.True);

        binding.Alive = false;

        Assert.That(session.ProbeIsAlive(), Is.False, "A dead binding must report as not alive.");
    }

    [Test]
    public void Invoke_checks_liveness_inside_binding_dispatcher()
    {
        var scene = new Scene(1920, 1080, "Scene");
        using RecordingPipeline recording = RecordingPipeline.Create(scene);
        var binding = new GuardedFakeLiveBinding(scene, recording.History);
        LiveEditingSession session = LiveEditingSession.Create(binding);
        binding.RequireDispatchedAccess = true;

        Assert.DoesNotThrow(() => session.Invoke(() => { }));
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

        public ValueTask<TResult> ExecuteHistoryMutationAsync<TResult>(
            Func<HistoryManager, bool> shouldPause,
            Func<HistoryManager, TResult> operation,
            CancellationToken cancellationToken = default,
            TimeSpan? sessionWait = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TResult result = default!;
            Invoke(() =>
            {
                HistoryManager activeHistory = ActiveHistory ?? throw new SessionUnavailableException();
                _ = shouldPause(activeHistory);
                activeHistory.FlushPendingMutations();
                result = operation(activeHistory);
            });
            return ValueTask.FromResult(result);
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
