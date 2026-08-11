using Avalonia.Headless.NUnit;
using Beutl.Api.Services;
using Beutl.Testing.Headless;
using Beutl.Services;
using Beutl.ViewModels;
using Beutl.ViewModels.Dialogs;
using Beutl.ViewModels.SettingsPages;
using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class AiUsageViewModelTests
{
    [AvaloniaTest]
    public async Task VideoDurationOptions_MatchDefaultProviderCapabilities()
    {
        await TestReset.ResetShellAsync();
        Beutl.Api.BeutlApiApplication clients = TestShell.MainViewModel._beutlClients;
        await using var viewModel = new AiVideoGenerationDialogViewModel(
            clients.GetResource<IAiEntitlementService>(),
            new AiPlanCoordinator(clients, clients.GetResource<IAiEntitlementService>()),
            clients.GetResource<IAiVideoService>(),
            clients.GetResource<IAuthenticatedContentService>(),
            clients.GetResource<IAiJobKindRegistry>());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(viewModel.DurationOptions.Select(option => option.Seconds),
                Is.EqualTo(new[] { 4, 6, 8 }));
            Assert.That(viewModel.SelectedDuration.Value.Seconds, Is.EqualTo(6));
        }
    }

    [Test]
    public void EntitlementSnapshot_UpdatesReadOnlyUsageAndCapability()
    {
        using var entitlements = new ReactivePropertySlim<AiEntitlements?>();
        using var viewModel = new AiUsageViewModel(entitlements);

        entitlements.Value = CreateEntitlements(
            canUseAi: true,
            used: 125,
            limit: 500,
            additionalCredits: 40,
            additionalCreditDebt: 15);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(viewModel.CanUseAi.Value, Is.True);
            Assert.That(viewModel.HasSnapshot.Value, Is.True);
            Assert.That(viewModel.UsagePercent.Value, Is.EqualTo(25));
            Assert.That(viewModel.RemainingPercent.Value, Is.EqualTo(75));
            Assert.That(viewModel.IsMonthlyAllowanceExhausted.Value, Is.False);
            Assert.That(viewModel.HasAdditionalCredits.Value, Is.True);
            Assert.That(viewModel.HasAdditionalCreditDebt.Value, Is.True);
        }
    }

    [Test]
    public void PresentedUsage_ShowsProportionWithoutDisclosingRawUnits()
    {
        using var entitlements = new ReactivePropertySlim<AiEntitlements?>();
        using var viewModel = new AiUsageViewModel(entitlements);

        entitlements.Value = CreateEntitlements(
            canUseAi: true,
            used: 125,
            limit: 500,
            additionalCredits: 40,
            additionalCreditDebt: 15);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(viewModel.UsagePercent.Value, Is.EqualTo(25));
            Assert.That(viewModel.MonthlyUsageText.Value, Does.Contain("25"));
            Assert.That(viewModel.MonthlyRemainingText.Value, Does.Contain("75"));
            Assert.That(
                viewModel.MonthlyUsageText.Value,
                Does.Not.Contain("500"),
                "The allowance size must stay hidden so per-operation cost cannot be derived.");
            Assert.That(
                viewModel.AdditionalCreditsText.Value,
                Does.Contain("40"),
                "Purchased credits are a paid-for quantity and stay visible.");
            Assert.That(viewModel.AdditionalCredits.Value, Is.EqualTo(40));
            Assert.That(viewModel.AdditionalCreditDebtText.Value, Does.Not.Contain("15"));
            Assert.That(viewModel.HasAdditionalCredits.Value, Is.True);
        }
    }

    [Test]
    public void ReplacingSharedSnapshot_UpdatesEveryPresentation()
    {
        using var entitlements = new ReactivePropertySlim<AiEntitlements?>(
            CreateEntitlements(canUseAi: true, used: 125, limit: 500, additionalCredits: 40));
        using var first = new AiUsageViewModel(entitlements);
        using var second = new AiUsageViewModel(entitlements);

        entitlements.Value = CreateEntitlements(canUseAi: true, used: 200, limit: 500, additionalCredits: 25);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.UsagePercent.Value, Is.EqualTo(40));
            Assert.That(second.UsagePercent.Value, Is.EqualTo(40));
            Assert.That(first.HasAdditionalCredits.Value, Is.True);
            Assert.That(second.HasAdditionalCredits.Value, Is.True);
        }
    }

    [Test]
    public void ClearingSnapshot_RemovesPreviousAccountsBalance()
    {
        using var entitlements = new ReactivePropertySlim<AiEntitlements?>(
            CreateEntitlements(canUseAi: true, used: 125, limit: 500, additionalCredits: 40));
        using var viewModel = new AiUsageViewModel(entitlements);

        entitlements.Value = null;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(viewModel.CanUseAi.Value, Is.False);
            Assert.That(viewModel.HasSnapshot.Value, Is.False);
            Assert.That(viewModel.UsagePercent.Value, Is.Zero);
            Assert.That(viewModel.RemainingPercent.Value, Is.EqualTo(100));
            Assert.That(viewModel.HasAdditionalCredits.Value, Is.False);
            Assert.That(viewModel.HasAdditionalCreditDebt.Value, Is.False);
        }
    }

    [TestCase("active", true)]
    [TestCase("past_due", true)]
    [TestCase("unpaid", true)]
    [TestCase("canceled", false)]
    [TestCase("incomplete_expired", false)]
    [TestCase(null, false)]
    public void SubscriptionManagement_RemainsAvailableForRecoverableStatuses(
        string? status,
        bool expected)
    {
        Assert.That(AccountSettingsPageViewModel.IsManageableSubscription(status), Is.EqualTo(expected));
    }

    private static AiEntitlements CreateEntitlements(
        bool canUseAi,
        int used,
        int limit,
        int additionalCredits,
        int additionalCreditDebt = 0)
    {
        return new AiEntitlements(
            canUseAi ? "pro" : null,
            canUseAi ? "active" : null,
            null,
            null,
            false,
            canUseAi,
            new AiBalance(
                new AiMonthlyUsage(
                    ToPercent(used, limit),
                    100 - ToPercent(used, limit),
                    limit > 0 && used >= limit),
                additionalCredits,
                additionalCreditDebt > 0),
            new AiOperationAvailability([]));
    }

    private static int ToPercent(int used, int limit)
        => limit <= 0 ? 0 : Math.Clamp((int)Math.Round(used * 100.0 / limit), 0, 100);
}
