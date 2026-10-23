using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tests.Helpers;
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
            Assert.That(scene.Duration.Value, Is.EqualTo(MediaTime.FromSeconds(8)));
            // Resolve, change-set validation, and the write must share a single dispatch so a UI edit
            // cannot slip between the check and the write.
            Assert.That(binding.InvokeCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void ApplyValidated_rejects_without_mutating_when_validation_fails()
    {
        (LiveEditingSession session, FakeLiveBinding binding, Scene scene) = CreateLiveSession();
        MediaTime original = scene.Duration.Value;
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
            Assert.That(scene.Duration.Value, Is.EqualTo(original));
            Assert.That(binding.InvokeCount, Is.EqualTo(1));
        });
    }

    private static (LiveEditingSession Session, FakeLiveBinding Binding, Scene Scene) CreateLiveSession()
    {
        Scene scene = TestWorkspace.CreateScene();
        RecordingPipeline recording = RecordingPipeline.Create(scene);
        var binding = new FakeLiveBinding(scene, recording.History);
        LiveEditingSession session = LiveEditingSession.Create(binding);
        return (session, binding, scene);
    }
}
