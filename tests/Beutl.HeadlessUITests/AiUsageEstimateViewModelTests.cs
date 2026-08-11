using System.Reactive.Subjects;
using Beutl.Api.Services;
using Beutl.ViewModels;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class AiUsageEstimateViewModelTests
{
    [Test]
    public void Estimate_UsesMonthlyThenTopUpAndDetectsShortfall()
    {
        // The monthly allowance is spent, so purchased credits cover the run.
        using var entitlements = new BehaviorSubject<AiEntitlements?>(
            CreateEntitlements(isExhausted: true, hasAdditionalCredits: true));
        using var available = new BehaviorSubject<bool>(true);
        using var usage = new AiUsageViewModel(entitlements);
        using var estimate = new AiUsageEstimateViewModel(usage, available);

        Assert.Multiple(() =>
        {
            Assert.That(estimate.CanAfford.Value, Is.True);
            Assert.That(estimate.IsInsufficient.Value, Is.False);
            Assert.That(
                estimate.Explanation.Value,
                Is.EqualTo(Beutl.Language.Strings.AiEstimatedUsageTopUp));
            Assert.That(
                estimate.Explanation.Value,
                Does.Not.Contain("20"),
                "The explanation must not disclose the per-operation usage cost.");
        });

        available.OnNext(false);

        Assert.Multiple(() =>
        {
            Assert.That(estimate.CanAfford.Value, Is.False);
            Assert.That(estimate.IsInsufficient.Value, Is.True);
            Assert.That(
                estimate.Explanation.Value,
                Is.EqualTo(Beutl.Language.Strings.AiEstimatedUsageInsufficient));
            Assert.That(estimate.Explanation.Value, Does.Not.Contain("10"));
        });
    }

    [Test]
    public void MissingAuthoritativePrice_DisablesExecutionAndShowsUnavailableState()
    {
        // Without an active plan the server reports nothing as startable.
        using var entitlements = new BehaviorSubject<AiEntitlements?>(
            CreateEntitlements(isExhausted: false, hasAdditionalCredits: false, canUseAi: false));
        using var available = new BehaviorSubject<bool>(false);
        using var usage = new AiUsageViewModel(entitlements);
        using var estimate = new AiUsageEstimateViewModel(usage, available);

        Assert.Multiple(() =>
        {
            Assert.That(estimate.IsAvailable.Value, Is.False);
            Assert.That(estimate.CanAfford.Value, Is.False);
            Assert.That(estimate.IsInsufficient.Value, Is.False);
            Assert.That(
                estimate.Summary.Value,
                Is.EqualTo(Beutl.Language.Strings.AiPricingUnavailable));
        });
    }

    private static AiEntitlements CreateEntitlements(
        bool isExhausted,
        bool hasAdditionalCredits,
        bool canUseAi = true)
        => new(
            canUseAi ? "pro" : null,
            canUseAi ? "active" : null,
            new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
            false,
            canUseAi,
            new AiBalance(
                new AiMonthlyUsage(isExhausted ? 100 : 20, isExhausted ? 0 : 80, isExhausted),
                hasAdditionalCredits ? 100 : 0,
                false),
            new AiOperationAvailability([]));
}
