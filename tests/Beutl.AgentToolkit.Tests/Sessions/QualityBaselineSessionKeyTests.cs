using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Sessions;

namespace Beutl.AgentToolkit.Tests.Sessions;

public sealed class QualityBaselineSessionKeyTests
{
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
