using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Tests.Sessions;

public sealed class QualityBaselineSessionKeyTests
{
    [Test]
    public void Session_key_distinguishes_two_sessions_sharing_a_scene_id()
    {
        var manager = new AgentSessionManager();
        // save-as/copy preserves the persisted Scene.Id but writes a different file, so two distinct
        // sessions can carry the same Root.Id under different URIs; the key must separate them so
        // cached plans/baselines cannot cross over. (AgentToolkitTestSession assigns each a unique URI.)
        var sharedId = Guid.NewGuid();
        using var first = new AgentToolkitTestSession(new Scene(16, 9, "first") { Id = sharedId });
        using var second = new AgentToolkitTestSession(new Scene(16, 9, "second") { Id = sharedId });

        string firstKey = manager.GetSessionKey(first);
        string secondKey = manager.GetSessionKey(second);

        Assert.Multiple(() =>
        {
            Assert.That(first.Root.Id, Is.EqualTo(second.Root.Id));
            Assert.That(first.Root.Uri, Is.Not.EqualTo(second.Root.Uri));
            Assert.That(firstKey, Is.Not.EqualTo(secondKey));
            Assert.That(firstKey, Does.Contain(first.Root.Uri!.ToString()));
            Assert.That(secondKey, Does.Contain(second.Root.Uri!.ToString()));
        });
    }

    [Test]
    public void Baseline_is_stored_under_its_captured_session_key_not_the_current_one()
    {
        var manager = new AgentSessionManager();
        string currentKey = manager.CurrentSessionKey;

        // Captured before an async render on a session that has since been switched away.
        var baseline = new QualityReviewBaseline("captured-other-session", DateTimeOffset.MinValue, [], null!, null!, []);
        manager.StoreQualityReviewBaseline(baseline);

        // Stored under the captured key, so a lookup under the current key must miss instead of
        // returning another project's metrics.
        ReconcileException ex = Assert.Throws<ReconcileException>(() => manager.GetQualityReviewBaseline())!;
        Assert.That(ex.Error.Code, Is.EqualTo(ErrorCode.StaleHandle));

        QualityReviewBaseline matching = baseline with { SessionKey = currentKey };
        manager.StoreQualityReviewBaseline(matching);
        Assert.That(manager.GetQualityReviewBaseline(), Is.SameAs(matching));
    }
}
