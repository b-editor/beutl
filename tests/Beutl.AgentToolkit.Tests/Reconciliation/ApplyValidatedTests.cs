using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Sessions;
using Beutl.Editor;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Tests.Reconciliation;

public sealed class ApplyValidatedTests
{
    [Test]
    public void ApplyValidated_resolves_validates_and_applies_in_one_dispatch()
    {
        (LiveEditingSession session, FakeLiveBinding binding, Scene scene) = CreateLiveSession();
        var reconciler = new Reconciler();

        ReconcileResult result = reconciler.ApplyValidated(
            session,
            current =>
            {
                current[nameof(Scene.Duration)] = TimeSpan.FromSeconds(8).ToString("c");
                return (current, null);
            },
            _ => null);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(scene.Duration, Is.EqualTo(TimeSpan.FromSeconds(8)));
            // Resolve, change-set validation, and the write must share a single dispatch so a UI edit
            // cannot slip between the check and the write.
            Assert.That(binding.InvokeCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void ApplyValidated_rejects_without_mutating_when_validation_fails()
    {
        (LiveEditingSession session, FakeLiveBinding binding, Scene scene) = CreateLiveSession();
        TimeSpan original = scene.Duration;
        var reconciler = new Reconciler();

        Assert.Throws<ReconcileException>(() => reconciler.ApplyValidated(
            session,
            current =>
            {
                current[nameof(Scene.Duration)] = TimeSpan.FromSeconds(8).ToString("c");
                return (current, null);
            },
            _ => new ToolError(ErrorCode.ValidationRejected, "rejected by test")));

        Assert.Multiple(() =>
        {
            Assert.That(scene.Duration, Is.EqualTo(original));
            Assert.That(binding.InvokeCount, Is.EqualTo(1));
        });
    }

    private static (LiveEditingSession Session, FakeLiveBinding Binding, Scene Scene) CreateLiveSession()
    {
        string dir = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var scene = new Scene(1920, 1080, "Scene")
        {
            Uri = new Uri(Path.Combine(dir, "Scene.scene"))
        };
        RecordingPipeline recording = RecordingPipeline.Create(scene);
        var binding = new FakeLiveBinding(scene, recording.History);
        LiveEditingSession session = new LiveSessionSource().Attach(binding);
        return (session, binding, scene);
    }

    private sealed class FakeLiveBinding(Scene scene, HistoryManager history) : ILiveSessionBinding
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
