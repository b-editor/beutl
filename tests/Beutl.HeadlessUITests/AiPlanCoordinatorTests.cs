using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Beutl.Api;
using Beutl.Api.Services;
using Beutl.Services;
using Beutl.Testing.Headless;
using Beutl.Views.Tools;
using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class AiPlanCoordinatorTests
{
    [Test]
    public async Task OpenPages_RefreshesOnlyOnceAfterReturn()
    {
        var entitlements = new StubEntitlementService();
        var opened = new List<Uri>();
        var coordinator = new AiPlanCoordinator(
            entitlements,
            opened.Add,
            () => "ja",
            new Uri("https://beutl.beditor.net/"));
        int refreshedEvents = 0;
        coordinator.Refreshed += (_, _) => refreshedEvents++;

        coordinator.OpenAiPlan();
        coordinator.OpenAccountSettings();
        bool refreshed = await coordinator.RefreshIfPendingAsync(CancellationToken.None);
        bool noPendingRefresh = await coordinator.RefreshIfPendingAsync(CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(opened, Is.EqualTo(new[]
            {
                new Uri("https://beutl.beditor.net/ja/account/manage/ai-plan"),
                new Uri("https://beutl.beditor.net/account/manage"),
            }));
            Assert.That(entitlements.RefreshCount, Is.EqualTo(1));
            Assert.That(refreshed, Is.True);
            Assert.That(noPendingRefresh, Is.False);
            Assert.That(refreshedEvents, Is.EqualTo(1));
        }
    }

    // A build pointed at a local server has to open that server's plan pages;
    // opening the live site would show a plan this build cannot act on.
    [Test]
    public void OpenPages_FollowTheConfiguredApiOrigin()
    {
        var opened = new List<Uri>();
        var coordinator = new AiPlanCoordinator(
            new StubEntitlementService(),
            opened.Add,
            () => "en");

        coordinator.OpenAiPlan();
        coordinator.OpenAccountSettings();

        var expected = new Uri(BeutlApiApplication.BaseUrl, UriKind.Absolute);
        Assert.That(opened, Is.EqualTo(new[]
        {
            new Uri(expected, "en/account/manage/ai-plan"),
            new Uri(expected, "account/manage"),
        }));
    }

    [Test]
    public void FailedRefresh_RemainsPendingForTheNextActivation()
    {
        var entitlements = new StubEntitlementService
        {
            Failure = new InvalidOperationException("offline"),
        };
        var coordinator = new AiPlanCoordinator(
            entitlements,
            _ => { },
            () => "en");
        int refreshedEvents = 0;
        coordinator.Refreshed += (_, _) => refreshedEvents++;
        coordinator.OpenAiPlan();

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await coordinator.RefreshIfPendingAsync(CancellationToken.None));
        entitlements.Failure = null;
        bool refreshed = false;
        Assert.DoesNotThrowAsync(async () =>
        {
            refreshed = await coordinator.RefreshIfPendingAsync(CancellationToken.None);
        });
        Assert.That(entitlements.RefreshCount, Is.EqualTo(2));
        Assert.That(refreshed, Is.True);
        Assert.That(refreshedEvents, Is.EqualTo(1));
    }

    [Test]
    public async Task SuccessfulRefresh_ContinuesAfterAnEventSubscriberThrows()
    {
        var entitlements = new StubEntitlementService();
        var coordinator = new AiPlanCoordinator(
            entitlements,
            _ => { },
            () => "en");
        int laterEvents = 0;
        coordinator.Refreshed += (_, _) => throw new InvalidOperationException("observer failed");
        coordinator.Refreshed += (_, _) => laterEvents++;
        coordinator.OpenAiPlan();

        bool refreshed = await coordinator.RefreshIfPendingAsync(CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(refreshed, Is.True);
            Assert.That(entitlements.RefreshCount, Is.EqualTo(1));
            Assert.That(laterEvents, Is.EqualTo(1));
        }
    }

    [AvaloniaTest]
    public async Task ReturnRefresh_LoadCyclesDoNotDuplicateAndDisposalStopsActivationCallbacks()
    {
        var coordinator = new RecordingPlanCoordinator();
        var control = new Border();
        var host = new Window { Content = control };
        var other = new Window();
        int refreshCallbacks = 0;
        IDisposable subscription = AiPlanReturnRefresh.Attach(
            control,
            coordinator,
            () => refreshCallbacks++);
        try
        {
            host.Show();
            other.Show();
            HeadlessTestHelpers.Settle();

            int initialCount = coordinator.RefreshCount;
            coordinator.HasPendingRefresh = true;
            other.Activate();
            host.Activate();
            await WaitUntilAsync(() => coordinator.RefreshCount == initialCount + 1);
            await WaitUntilAsync(() => refreshCallbacks == 1);

            int callbacksBeforeUnrelatedActivation = refreshCallbacks;
            int refreshesBeforeUnrelatedActivation = coordinator.RefreshCount;
            other.Activate();
            host.Activate();
            await WaitUntilAsync(() => coordinator.RefreshCount == refreshesBeforeUnrelatedActivation + 1);
            Assert.That(refreshCallbacks, Is.EqualTo(callbacksBeforeUnrelatedActivation));

            host.Content = null;
            HeadlessTestHelpers.Settle();
            host.Content = control;
            HeadlessTestHelpers.Settle();
            int callbacksBeforeSecondActivation = refreshCallbacks;
            int refreshesBeforeSecondActivation = coordinator.RefreshCount;
            coordinator.HasPendingRefresh = true;
            other.Activate();
            host.Activate();
            await WaitUntilAsync(() => coordinator.RefreshCount == refreshesBeforeSecondActivation + 1);
            await WaitUntilAsync(() => refreshCallbacks == callbacksBeforeSecondActivation + 1);

            subscription.Dispose();
            int disposedCount = coordinator.RefreshCount;
            other.Activate();
            host.Activate();
            await Task.Delay(25);
            HeadlessTestHelpers.Settle();
            Assert.That(coordinator.RefreshCount, Is.EqualTo(disposedCount));
        }
        finally
        {
            subscription.Dispose();
            other.Close();
            host.Close();
        }
    }

    [AvaloniaTest]
    public async Task ReturnRefresh_BroadcastsOneSuccessfulRefreshToAllLoadedControls()
    {
        var coordinator = new RecordingPlanCoordinator();
        var first = new Border();
        var second = new Border();
        var host = new Window
        {
            Content = new StackPanel { Children = { first, second } },
        };
        var other = new Window();
        int firstCallbacks = 0;
        int secondCallbacks = 0;
        using IDisposable firstSubscription = AiPlanReturnRefresh.Attach(
            first,
            coordinator,
            () => firstCallbacks++);
        using IDisposable secondSubscription = AiPlanReturnRefresh.Attach(
            second,
            coordinator,
            () => secondCallbacks++);
        try
        {
            host.Show();
            other.Show();
            HeadlessTestHelpers.Settle();
            coordinator.HasPendingRefresh = true;

            other.Activate();
            host.Activate();
            await WaitUntilAsync(() => firstCallbacks == 1 && secondCallbacks == 1);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(firstCallbacks, Is.EqualTo(1));
                Assert.That(secondCallbacks, Is.EqualTo(1));
            }
        }
        finally
        {
            other.Close();
            host.Close();
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 100 && !condition(); attempt++)
        {
            HeadlessTestHelpers.Settle();
            await Task.Delay(10);
        }

        Assert.That(condition(), Is.True);
    }

    private sealed class StubEntitlementService : IAiEntitlementService
    {
        public IReadOnlyReactiveProperty<AiEntitlements?> Entitlements { get; }
            = new ReactivePropertySlim<AiEntitlements?>();

        public int RefreshCount { get; private set; }

        public Exception? Failure { get; set; }

        public Task<AiEntitlements?> RefreshAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RefreshCount++;
            return Failure is null
                ? Task.FromResult<AiEntitlements?>(null)
                : Task.FromException<AiEntitlements?>(Failure);
        }

    }

    private sealed class RecordingPlanCoordinator : IAiPlanCoordinator
    {
        public int RefreshCount { get; private set; }

        public bool HasPendingRefresh { get; set; }

        public event EventHandler? Refreshed;

        public void OpenAccountSettings()
        {
        }

        public void OpenAiPlan()
        {
        }

        public Task<bool> RefreshIfPendingAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RefreshCount++;
            bool result = HasPendingRefresh;
            HasPendingRefresh = false;
            if (result)
                Refreshed?.Invoke(this, EventArgs.Empty);
            return Task.FromResult(result);
        }
    }
}
