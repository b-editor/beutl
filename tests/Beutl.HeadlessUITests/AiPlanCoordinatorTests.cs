using Avalonia.Controls;
using Avalonia.Headless.NUnit;
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
            () => "ja");

        coordinator.OpenAiPlan();
        coordinator.OpenAccountSettings();
        await coordinator.RefreshIfPendingAsync(CancellationToken.None);
        await coordinator.RefreshIfPendingAsync(CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(opened, Is.EqualTo(new[]
            {
                new Uri("https://beutl.beditor.net/ja/account/manage/ai-plan"),
                new Uri("https://beutl.beditor.net/account/manage"),
            }));
            Assert.That(entitlements.RefreshCount, Is.EqualTo(1));
        }
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
        coordinator.OpenAiPlan();

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await coordinator.RefreshIfPendingAsync(CancellationToken.None));
        entitlements.Failure = null;
        Assert.DoesNotThrowAsync(async () =>
            await coordinator.RefreshIfPendingAsync(CancellationToken.None));
        Assert.That(entitlements.RefreshCount, Is.EqualTo(2));
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
            other.Activate();
            host.Activate();
            await WaitUntilAsync(() => coordinator.RefreshCount == initialCount + 1);

            host.Content = null;
            HeadlessTestHelpers.Settle();
            host.Content = control;
            HeadlessTestHelpers.Settle();
            int callbacksBeforeSecondActivation = refreshCallbacks;
            other.Activate();
            host.Activate();
            await WaitUntilAsync(() => coordinator.RefreshCount == initialCount + 2);
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

        public void OpenAccountSettings()
        {
        }

        public void OpenAiPlan()
        {
        }

        public Task RefreshIfPendingAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RefreshCount++;
            return Task.CompletedTask;
        }
    }
}
