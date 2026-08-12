using System.Net;
using System.Reflection;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Beutl.Api;
using Beutl.Api.Clients;
using Beutl.Api.Objects;
using Beutl.Api.Services;
using Beutl.Language;
using Beutl.Pages.SettingsPages;
using Beutl.Services;
using Beutl.Testing.Headless;
using Beutl.ViewModels.SettingsPages;
using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public sealed class AccountSettingsPageViewModelTests
{
    [AvaloniaTest]
    public async Task DelayedEntitlements_SignOutCancelsAndClearsLoadingState()
    {
        using var handler = new ControlledHandler(honorCancellation: true);
        using var httpClient = new HttpClient(handler);
        using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, "user-a");
        using var viewModel = new AccountSettingsPageViewModel(clients, CreateCoordinator(clients));
        await handler.WaitForRequestCountAsync(1);

        SetAuthenticatedUser(clients, null);

        await WaitUntilAsync(() => handler.Requests[0].CancellationToken.IsCancellationRequested);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(viewModel.IsAiPlanLoading.Value, Is.False);
            Assert.That(viewModel.AiPlanError.Value, Is.Null);
            Assert.That(handler.Requests, Has.Count.EqualTo(1));
        }
    }

    [AvaloniaTest]
    public async Task PortalCancellation_ReportsTheScheduledEndWhileStillActive()
    {
        // Stripe keeps the status on "active" until the period ends, so the
        // scheduled flag is the only signal that the plan will stop.
        using var handler = new ControlledHandler(honorCancellation: false);
        using var httpClient = new HttpClient(handler);
        using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, "user-a");
        using var viewModel = new AccountSettingsPageViewModel(clients, CreateCoordinator(clients));
        await handler.WaitForRequestCountAsync(1);

        handler.Complete(
            0,
            JsonResponse(HttpStatusCode.OK, EntitlementsJson(cancelAtPeriodEnd: true)));
        await WaitUntilAsync(() => !viewModel.IsAiPlanLoading.Value);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                viewModel.SubscriptionStatus.Value,
                Is.EqualTo(SettingsStrings.AiPlan_SubscriptionStatus_CancelScheduled));
            Assert.That(viewModel.HasCancelScheduledNotice.Value, Is.True);
            Assert.That(viewModel.CancelScheduledNotice.Value, Is.Not.Null);
            Assert.That(
                viewModel.AiPlanActionText.Value,
                Is.EqualTo(SettingsStrings.AiPlan_ManageAndTopUp),
                "The user must still be able to resume the subscription.");
        }
    }

    [AvaloniaTest]
    public async Task ActiveSubscription_DoesNotClaimAScheduledCancellation()
    {
        using var handler = new ControlledHandler(honorCancellation: false);
        using var httpClient = new HttpClient(handler);
        using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, "user-a");
        using var viewModel = new AccountSettingsPageViewModel(clients, CreateCoordinator(clients));
        await handler.WaitForRequestCountAsync(1);

        handler.Complete(0, JsonResponse(HttpStatusCode.OK, EntitlementsJson()));
        await WaitUntilAsync(() => !viewModel.IsAiPlanLoading.Value);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                viewModel.SubscriptionStatus.Value,
                Is.EqualTo(SettingsStrings.AiPlan_SubscriptionStatus_Active));
            Assert.That(viewModel.HasCancelScheduledNotice.Value, Is.False);
        }
    }

    [AvaloniaTest]
    public async Task ReturningFromThePlanPage_RereadsTheEntitlements()
    {
        // A cancellation made in the Stripe customer portal happens in a browser,
        // so the app only learns about it by asking the server again.
        using var handler = new ControlledHandler(honorCancellation: false);
        using var httpClient = new HttpClient(handler);
        using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, "user-a");
        var coordinator = CreateCoordinator(clients);
        using var viewModel = new AccountSettingsPageViewModel(clients, coordinator);
        await handler.WaitForRequestCountAsync(1);
        handler.Complete(0, JsonResponse(HttpStatusCode.OK, EntitlementsJson()));
        await WaitUntilAsync(() => !viewModel.IsAiPlanLoading.Value);

        // Returning without having opened the plan must not cost a request.
        viewModel.NotifyReturnedToApplication();
        await Task.Delay(25);
        HeadlessTestHelpers.Settle();
        Assert.That(handler.Requests, Has.Count.EqualTo(1));

        coordinator.OpenAiPlan();
        viewModel.NotifyReturnedToApplication();
        await handler.WaitForRequestCountAsync(2);
        handler.Complete(1, JsonResponse(HttpStatusCode.OK, EntitlementsJson(cancelAtPeriodEnd: true)));
        await WaitUntilAsync(() =>
            viewModel.SubscriptionStatus.Value
                == SettingsStrings.AiPlan_SubscriptionStatus_CancelScheduled);

        // The pending flag is consumed, so focusing the app again is free.
        viewModel.NotifyReturnedToApplication();
        await Task.Delay(25);
        HeadlessTestHelpers.Settle();
        Assert.That(handler.Requests, Has.Count.EqualTo(2));
    }

    [AvaloniaTest]
    public async Task LoadedPage_WindowActivationRefreshesAfterBrowserReturn()
    {
        using var handler = new ControlledHandler(honorCancellation: false);
        using var httpClient = new HttpClient(handler);
        using var clients = new BeutlApiApplication(
            httpClient,
            new ExtensionProvider());
        SetAuthenticatedUser(clients, "user-a");
        var coordinator = CreateCoordinator(clients);
        using var viewModel = new AccountSettingsPageViewModel(clients, coordinator);
        await handler.WaitForRequestCountAsync(1);
        handler.Complete(0, JsonResponse(HttpStatusCode.OK, EntitlementsJson()));
        await WaitUntilAsync(() => !viewModel.IsAiPlanLoading.Value);

        var page = new AccountSettingsPage { DataContext = viewModel };
        var accountWindow = new Window { Content = page };
        var browserWindow = new Window();
        try
        {
            accountWindow.Show();
            browserWindow.Show();
            HeadlessTestHelpers.Settle();

            coordinator.OpenAiPlan();
            accountWindow.Activate();
            HeadlessTestHelpers.Settle();
            await handler.WaitForRequestCountAsync(2);
            handler.Complete(
                1,
                JsonResponse(HttpStatusCode.OK, EntitlementsJson(cancelAtPeriodEnd: true)));
            await WaitUntilAsync(() =>
                viewModel.SubscriptionStatus.Value
                    == SettingsStrings.AiPlan_SubscriptionStatus_CancelScheduled);

            Assert.That(handler.Requests, Has.Count.EqualTo(2));
        }
        finally
        {
            browserWindow.Close();
            accountWindow.Close();
        }
    }

    [AvaloniaTest]
    public async Task DelayedEntitlements_AccountSwitchIgnoresStaleFailure()
    {
        using var handler = new ControlledHandler(honorCancellation: false);
        using var httpClient = new HttpClient(handler);
        using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, "user-a");
        using var viewModel = new AccountSettingsPageViewModel(clients, CreateCoordinator(clients));
        await handler.WaitForRequestCountAsync(1);

        SetAuthenticatedUser(clients, "user-b");
        handler.Complete(0, JsonResponse(HttpStatusCode.InternalServerError, "{}"));
        await handler.WaitForRequestCountAsync(2);
        handler.Complete(1, JsonResponse(HttpStatusCode.OK, EntitlementsJson()));
        await WaitUntilAsync(() => !viewModel.IsAiPlanLoading.Value);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(viewModel.AiPlanError.Value, Is.Null);
            Assert.That(viewModel.PlanName.Value, Is.EqualTo(SettingsStrings.AiPlan_Pro));
            Assert.That(clients.AuthenticatedUser.Value?.Profile.Id, Is.EqualTo("user-b"));
        }
    }

    [AvaloniaTest]
    public async Task DelayedEntitlements_CurrentFailurePublishesLocalizedError()
    {
        using var handler = new ControlledHandler(honorCancellation: false);
        using var httpClient = new HttpClient(handler);
        using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, "user-a");
        using var viewModel = new AccountSettingsPageViewModel(clients, CreateCoordinator(clients));
        await handler.WaitForRequestCountAsync(1);

        handler.Complete(0, JsonResponse(HttpStatusCode.InternalServerError, "{}"));
        await WaitUntilAsync(() => !viewModel.IsAiPlanLoading.Value);

        Assert.That(viewModel.AiPlanError.Value, Is.Not.Null.And.Not.Empty);
    }

    [AvaloniaTest]
    public async Task Dispose_CancelsDelayedEntitlementsAndStopsAccountObservation()
    {
        using var handler = new ControlledHandler(honorCancellation: true);
        using var httpClient = new HttpClient(handler);
        using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(clients, "user-a");
        var viewModel = new AccountSettingsPageViewModel(clients, CreateCoordinator(clients));
        await handler.WaitForRequestCountAsync(1);

        viewModel.Dispose();
        await WaitUntilAsync(() => handler.Requests[0].CancellationToken.IsCancellationRequested);
        SetAuthenticatedUser(clients, "user-b");
        await Task.Delay(25);
        HeadlessTestHelpers.Settle();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(handler.Requests, Has.Count.EqualTo(1));
            Assert.DoesNotThrow(viewModel.Dispose);
        }
    }

    private static void SetAuthenticatedUser(BeutlApiApplication clients, string? userId)
    {
        FieldInfo field = typeof(BeutlApiApplication).GetField(
            "_authenticatedUser",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        var state = (ReactivePropertySlim<AuthenticatedUser?>)field.GetValue(clients)!;
        if (userId is null)
        {
            state.Value = null;
            return;
        }

        var profile = new Profile(new ProfileResponse
        {
            Id = userId,
            Name = userId,
            DisplayName = userId,
            Bio = null,
            IconId = null,
            IconUrl = null,
        }, clients);
        state.Value = new AuthenticatedUser(profile, new AuthResponse
        {
            Token = $"token-{userId}",
            RefreshToken = $"refresh-{userId}",
            Expiration = DateTime.UtcNow.AddHours(1),
        }, clients, DateTime.UtcNow);
    }

    private static AiPlanCoordinator CreateCoordinator(BeutlApiApplication clients)
        => new(
            clients.GetResource<IAiEntitlementService>(),
            _ => { },
            () => "en");

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json)
        => new(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    private static string EntitlementsJson(bool cancelAtPeriodEnd = false) => $$"""
        {
          "plan": "pro",
          "subscriptionStatus": "active",
          "currentPeriodStart": "2026-08-01T00:00:00Z",
          "currentPeriodEnd": "2026-09-01T00:00:00Z",
          "cancelAtPeriodEnd": {{(cancelAtPeriodEnd ? "true" : "false")}},
          "canUseAi": true,
          "balance": {
            "monthlyUsage": {
              "usedPercent": 2,
              "remainingPercent": 98,
              "isExhausted": false
            },
            "additionalCredits": 5,
            "hasAdditionalCreditDebt": false
          },
          "availability": { "image.generate": true }
        }
        """;

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            HeadlessTestHelpers.Settle();
            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class ControlledHandler(bool honorCancellation) : HttpMessageHandler
    {
        private readonly Lock _gate = new();
        private readonly SemaphoreSlim _requestSignal = new(0);
        private readonly List<PendingRequest> _requests = [];

        public IReadOnlyList<PendingRequest> Requests
        {
            get
            {
                lock (_gate)
                {
                    return _requests.ToArray();
                }
            }
        }

        public void Complete(int index, HttpResponseMessage response)
        {
            PendingRequest request;
            lock (_gate)
            {
                request = _requests[index];
            }
            response.RequestMessage = request.Request;
            request.Completion.TrySetResult(response);
        }

        public async Task WaitForRequestCountAsync(int count)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (Requests.Count < count)
            {
                await _requestSignal.WaitAsync(timeout.Token);
            }
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var pending = new PendingRequest(request, cancellationToken);
            if (honorCancellation)
            {
                pending.CancellationRegistration = cancellationToken.Register(
                    () => pending.Completion.TrySetCanceled(cancellationToken));
            }
            lock (_gate)
            {
                _requests.Add(pending);
            }
            _requestSignal.Release();
            return pending.Completion.Task;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (PendingRequest request in Requests)
                {
                    request.CancellationRegistration.Dispose();
                    request.Completion.TrySetCanceled();
                }
                _requestSignal.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    private sealed class PendingRequest(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        public HttpRequestMessage Request { get; } = request;

        public CancellationToken CancellationToken { get; } = cancellationToken;

        public TaskCompletionSource<HttpResponseMessage> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CancellationTokenRegistration CancellationRegistration { get; set; }
    }
}
