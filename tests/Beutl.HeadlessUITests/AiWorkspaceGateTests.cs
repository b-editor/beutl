using System.Net;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Beutl.Api;
using Beutl.Api.Clients;
using Beutl.Api.Objects;
using Beutl.Api.Services;
using Beutl.Language;
using Beutl.Services;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.Dialogs;
using Beutl.ViewModels.Tools;
using Beutl.Views.Tools;
using Reactive.Bindings;
using Refit;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public sealed class AiWorkspaceGateTests
{
    [AvaloniaTest]
    [TestCase(false, null, true, false, true)]
    // A signed-in account without a snapshot is still loading, so the content stays visible.
    [TestCase(true, null, false, false, false)]
    [TestCase(true, false, false, true, true)]
    [TestCase(true, true, false, false, false)]
    public async Task Gate_FollowsTheAccountAndItsPlan(
        bool signedIn, bool? canUseAi, bool requiresSignIn, bool requiresPlan, bool gateOpen)
    {
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync($"ai-gate-{signedIn}-{canUseAi}");
        using var auth = new ReactivePropertySlim<AuthenticatedUser?>(signedIn ? CreateUser("user-a") : null);
        using var entitlements = new ReactivePropertySlim<AiEntitlements?>(
            canUseAi is { } plan ? CreateEntitlements(canUseAi: plan) : null);
        var plans = new StubPlanCoordinator();
        await using var workspace = new AiWorkspaceViewModel(
            editor, _ => new StubPage(), entitlements, auth, plans, _ => Task.CompletedTask);

        HeadlessTestHelpers.Settle();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(workspace.RequiresSignIn.Value, Is.EqualTo(requiresSignIn));
            Assert.That(workspace.RequiresPlan.Value, Is.EqualTo(requiresPlan));
            Assert.That(workspace.IsGateOpen.Value, Is.EqualTo(gateOpen));
        }
    }

    [AvaloniaTest]
    public async Task SignIn_RefreshesEntitlementsAndShowsPlanGateForNonProAccount()
    {
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("ai-gate-sign-in");
        using var auth = new ReactivePropertySlim<AuthenticatedUser?>();
        var entitlements = new StubEntitlementService
        {
            // The user signs in without a plan: the refresh must surface that,
            // otherwise the gate would close onto the usable form.
            RefreshResult = CreateEntitlements(canUseAi: false),
        };
        var plans = new StubPlanCoordinator();
        AuthenticatedUser user = CreateUser("user-a");
        await using var workspace = new AiWorkspaceViewModel(
            editor,
            _ => new StubPage(),
            entitlements.Entitlements,
            auth,
            plans,
            _ =>
            {
                auth.Value = user;
                return Task.CompletedTask;
            },
            entitlements);

        Assert.That(workspace.RequiresSignIn.Value, Is.True);

        await workspace.SignIn.ExecuteAsync();
        HeadlessTestHelpers.Settle();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(entitlements.RefreshCount, Is.EqualTo(1));
            Assert.That(workspace.RequiresSignIn.Value, Is.False);
            Assert.That(workspace.RequiresPlan.Value, Is.True);
            Assert.That(workspace.IsGateOpen.Value, Is.True);
            Assert.That(workspace.IsSigningIn.Value, Is.False);
        }
    }

    [AvaloniaTest]
    public async Task SignInFailure_SurfacesLocalizedErrorAndReleasesTheButton()
    {
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("ai-gate-sign-in-failure");
        using var auth = new ReactivePropertySlim<AuthenticatedUser?>();
        using var entitlements = new ReactivePropertySlim<AiEntitlements?>();
        var plans = new StubPlanCoordinator();
        await using var workspace = new AiWorkspaceViewModel(
            editor,
            _ => new StubPage(),
            entitlements,
            auth,
            plans,
            _ => Task.FromException(new InvalidOperationException("sign-in failed")));

        await workspace.SignIn.ExecuteAsync();
        HeadlessTestHelpers.Settle();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(workspace.SignInError.Value, Is.EqualTo(MessageStrings.UnexpectedError));
            Assert.That(workspace.IsSigningIn.Value, Is.False);
            Assert.That(workspace.IsGateOpen.Value, Is.True);
        }
    }

    [AvaloniaTest]
    public async Task SignInRefreshFailure_KeepsGateOpenWithRetry()
    {
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("ai-gate-refresh-failure");
        using var auth = new ReactivePropertySlim<AuthenticatedUser?>();
        var entitlements = new StubEntitlementService
        {
            Failure = new InvalidOperationException("entitlements unavailable"),
        };
        var plans = new StubPlanCoordinator();
        AuthenticatedUser user = CreateUser("user-a");
        await using var workspace = new AiWorkspaceViewModel(
            editor,
            _ => new StubPage(),
            entitlements.Entitlements,
            auth,
            plans,
            _ =>
            {
                auth.Value = user;
                return Task.CompletedTask;
            },
            entitlements);

        await workspace.SignIn.ExecuteAsync();
        HeadlessTestHelpers.Settle();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(workspace.IsSignedIn.Value, Is.True);
            Assert.That(workspace.HasEntitlementsSnapshot.Value, Is.False);
            Assert.That(workspace.RequiresPlan.Value, Is.False);
            Assert.That(workspace.GateRefreshFailed.Value, Is.True);
            Assert.That(workspace.IsGateOpen.Value, Is.True);
            Assert.That(workspace.SignInError.Value, Is.EqualTo(MessageStrings.UnexpectedError));
        }

        // A retry that recovers the snapshot resolves the gate to the plan state.
        entitlements.Failure = null;
        entitlements.RefreshResult = CreateEntitlements(canUseAi: false);
        await workspace.RetryGateLoad.ExecuteAsync();
        HeadlessTestHelpers.Settle();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(entitlements.RefreshCount, Is.EqualTo(2));
            Assert.That(workspace.GateRefreshFailed.Value, Is.False);
            Assert.That(workspace.SignInError.Value, Is.Null);
            Assert.That(workspace.RequiresPlan.Value, Is.True);
            Assert.That(workspace.IsGateOpen.Value, Is.True);
        }
    }

    [AvaloniaTest]
    public async Task RetryWhileSignedOut_RunsSignInAgain()
    {
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("ai-gate-retry-signed-out");
        using var auth = new ReactivePropertySlim<AuthenticatedUser?>();
        var entitlements = new StubEntitlementService
        {
            RefreshResult = CreateEntitlements(canUseAi: false),
        };
        var plans = new StubPlanCoordinator();
        int signInCount = 0;
        await using var workspace = new AiWorkspaceViewModel(
            editor,
            _ => new StubPage(),
            entitlements.Entitlements,
            auth,
            plans,
            _ =>
            {
                signInCount++;
                auth.Value = CreateUser("user-a");
                return Task.CompletedTask;
            },
            entitlements);

        await workspace.RetryGateLoad.ExecuteAsync();
        HeadlessTestHelpers.Settle();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(signInCount, Is.EqualTo(1));
            Assert.That(workspace.RequiresPlan.Value, Is.True);
        }
    }

    [AvaloniaTest]
    public async Task StaleFailure_ClearedByNewSnapshotAndAccountChange()
    {
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("ai-gate-stale-failure");
        using var auth = new ReactivePropertySlim<AuthenticatedUser?>();
        var entitlements = new StubEntitlementService
        {
            Failure = new InvalidOperationException("entitlements unavailable"),
        };
        var plans = new StubPlanCoordinator();
        await using var workspace = new AiWorkspaceViewModel(
            editor,
            _ => new StubPage(),
            entitlements.Entitlements,
            auth,
            plans,
            _ =>
            {
                auth.Value = CreateUser("user-a");
                return Task.CompletedTask;
            },
            entitlements);

        await workspace.SignIn.ExecuteAsync();
        HeadlessTestHelpers.Settle();
        Assert.That(workspace.GateRefreshFailed.Value, Is.True);

        // The new account's snapshot supersedes the stale failure even though no
        // retry ran: the gate must follow the fresh snapshot.
        entitlements.PublishSnapshot(CreateEntitlements(canUseAi: true));
        HeadlessTestHelpers.Settle();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(workspace.GateRefreshFailed.Value, Is.False);
            Assert.That(workspace.SignInError.Value, Is.Null);
            Assert.That(workspace.IsGateOpen.Value, Is.False);
        }

        // Fail once more so a stale failure is pending, then switch accounts: the
        // switch drops the stale failure and automatically refreshes for the new
        // account instead of carrying the old error over.
        entitlements.PublishSnapshot(null);
        await workspace.RetryGateLoad.ExecuteAsync();
        HeadlessTestHelpers.Settle();
        Assert.That(workspace.GateRefreshFailed.Value, Is.True);

        entitlements.Failure = null;
        entitlements.RefreshResult = CreateEntitlements(canUseAi: true);
        int refreshesBeforeSwitch = entitlements.RefreshCount;
        auth.Value = CreateUser("user-b");
        await HeadlessTestHelpers.WaitUntilAsync(
            () => entitlements.RefreshCount == refreshesBeforeSwitch + 1, TimeSpan.FromSeconds(1));
        HeadlessTestHelpers.Settle();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(workspace.GateRefreshFailed.Value, Is.False);
            Assert.That(workspace.SignInError.Value, Is.Null);
            Assert.That(workspace.HasEntitlementsSnapshot.Value, Is.True);
            Assert.That(workspace.IsGateOpen.Value, Is.False);
        }
    }

    [AvaloniaTest]
    public async Task OpenAiPlan_NeverEscapesCoordinatorFailures()
    {
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("ai-gate-plan-failure");
        using var auth = new ReactivePropertySlim<AuthenticatedUser?>();
        using var entitlements = new ReactivePropertySlim<AiEntitlements?>();
        var plans = new StubPlanCoordinator
        {
            OpenPlanFailure = new InvalidOperationException("cannot open uri"),
        };
        await using var workspace = new AiWorkspaceViewModel(
            editor, _ => new StubPage(), entitlements, auth, plans, _ => Task.CompletedTask);

        Assert.DoesNotThrow(() => workspace.OpenAiPlan.Execute());
        Assert.That(plans.OpenPlanCount, Is.EqualTo(1));
    }

    [AvaloniaTest]
    public async Task SignIn_ReloadsActivePageModels()
    {
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("ai-gate-model-reload");
        using var auth = new ReactivePropertySlim<AuthenticatedUser?>();
        var entitlements = new StubEntitlementService
        {
            RefreshResult = CreateEntitlements(canUseAi: true),
        };
        var plans = new StubPlanCoordinator();
        var page = new ModelPage();
        AuthenticatedUser user = CreateUser("user-a");
        await using var workspace = new AiWorkspaceViewModel(
            editor,
            _ => page,
            entitlements.Entitlements,
            auth,
            plans,
            _ =>
            {
                auth.Value = user;
                return Task.CompletedTask;
            },
            entitlements);

        int initialReloads = page.RefreshModelsCount;
        await workspace.SignIn.ExecuteAsync();
        HeadlessTestHelpers.Settle();

        Assert.That(
            page.RefreshModelsCount,
            Is.GreaterThan(initialReloads),
            "A page built while signed out holds an empty catalog and must reload it for the new account.");
    }

    [AvaloniaTest]
    public async Task AccountChange_RefreshesEntitlements()
    {
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("ai-gate-account-change");
        using var auth = new ReactivePropertySlim<AuthenticatedUser?>(CreateUser("user-a"));
        var entitlements = new StubEntitlementService
        {
            RefreshResult = CreateEntitlements(canUseAi: true),
        };
        var plans = new StubPlanCoordinator();
        var page = new ModelPage();
        await using var workspace = new AiWorkspaceViewModel(
            editor,
            _ => page,
            entitlements.Entitlements,
            auth,
            plans,
            signIn: null,
            entitlements);

        int refreshesBeforeSwitch = entitlements.RefreshCount;
        auth.Value = CreateUser("user-b");
        await HeadlessTestHelpers.WaitUntilAsync(
            () => entitlements.RefreshCount == refreshesBeforeSwitch + 1, TimeSpan.FromSeconds(1));
        HeadlessTestHelpers.Settle();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(workspace.HasEntitlementsSnapshot.Value, Is.True);
            Assert.That(workspace.IsGateOpen.Value, Is.False);
        }
    }

    [AvaloniaTest]
    public async Task NullRefreshResult_KeepsGateOpenWithRetry()
    {
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("ai-gate-null-refresh");
        using var auth = new ReactivePropertySlim<AuthenticatedUser?>();
        // A 401 answers without throwing and publishes no snapshot.
        var entitlements = new StubEntitlementService { RefreshResult = null };
        var plans = new StubPlanCoordinator();
        AuthenticatedUser user = CreateUser("user-a");
        await using var workspace = new AiWorkspaceViewModel(
            editor,
            _ => new StubPage(),
            entitlements.Entitlements,
            auth,
            plans,
            _ =>
            {
                auth.Value = user;
                return Task.CompletedTask;
            },
            entitlements);

        await workspace.SignIn.ExecuteAsync();
        HeadlessTestHelpers.Settle();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(workspace.IsSignedIn.Value, Is.True);
            Assert.That(workspace.HasEntitlementsSnapshot.Value, Is.False);
            Assert.That(workspace.GateRefreshFailed.Value, Is.True);
            Assert.That(workspace.IsGateOpen.Value, Is.True);
            Assert.That(workspace.SignInError.Value, Is.EqualTo(MessageStrings.UnexpectedError));
        }
    }

    [AvaloniaTest]
    public async Task WorkspaceView_PlanReturn_KeepsTheGateOpenUntilEntitlementsArrive()
    {
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("ai-gate-plan-return");
        using var auth = new ReactivePropertySlim<AuthenticatedUser?>(CreateUser("user-a"));
        using var entitlements = new ReactivePropertySlim<AiEntitlements?>();
        var plans = new StubPlanCoordinator();
        // The pages are stubs, so a plan return only updates the gate.
        await using var workspace = new AiWorkspaceViewModel(
            editor, _ => new StubPage(), entitlements, auth, plans, _ => Task.CompletedTask);
        var view = new AiWorkspaceView { DataContext = workspace };
        var window = new Window { Content = view, Width = 400, Height = 600 };

        try
        {
            window.Show();
            HeadlessTestHelpers.Settle();

            plans.RaiseRefreshed();
            HeadlessTestHelpers.Settle();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(workspace.GateRefreshFailed.Value, Is.True);
                Assert.That(workspace.IsGateOpen.Value, Is.True);
            }

            entitlements.Value = CreateEntitlements(canUseAi: true);
            HeadlessTestHelpers.Settle();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(workspace.GateRefreshFailed.Value, Is.False);
                Assert.That(workspace.IsGateOpen.Value, Is.False);
            }
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task PlanReturnWithoutSnapshot_KeepsGateOpen()
    {
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("ai-gate-plan-return-null");
        using var auth = new ReactivePropertySlim<AuthenticatedUser?>(CreateUser("user-a"));
        using var entitlements = new ReactivePropertySlim<AiEntitlements?>();
        var plans = new StubPlanCoordinator();
        var page = new ModelPage();
        await using var workspace = new AiWorkspaceViewModel(
            editor, _ => page, entitlements, auth, plans, _ => Task.CompletedTask);
        var view = new AiWorkspaceView { DataContext = workspace };
        var window = new Window { Content = view, Width = 400, Height = 600 };

        try
        {
            window.Show();
            HeadlessTestHelpers.Settle();

            // A 401 on return answers without throwing, so no snapshot arrives.
            plans.RaiseRefreshed();
            HeadlessTestHelpers.Settle();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(workspace.GateRefreshFailed.Value, Is.True);
                Assert.That(workspace.IsGateOpen.Value, Is.True);
                Assert.That(
                    page.RefreshModelsCount,
                    Is.EqualTo(1),
                    "The generation page refreshes its own models; the workspace must not do it twice.");
            }
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task Retry_HidesSignInActionWhileRefreshing()
    {
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("ai-gate-retry-progress");
        using var auth = new ReactivePropertySlim<AuthenticatedUser?>();
        var entitlements = new StubEntitlementService
        {
            Failure = new InvalidOperationException("entitlements unavailable"),
        };
        var plans = new StubPlanCoordinator();
        AuthenticatedUser user = CreateUser("user-a");
        await using var workspace = new AiWorkspaceViewModel(
            editor,
            _ => new StubPage(),
            entitlements.Entitlements,
            auth,
            plans,
            _ =>
            {
                auth.Value = user;
                return Task.CompletedTask;
            },
            entitlements);

        await workspace.SignIn.ExecuteAsync();
        HeadlessTestHelpers.Settle();
        Assert.That(workspace.GateRefreshFailed.Value, Is.True);

        entitlements.Failure = null;
        var releaseRefresh = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        entitlements.RefreshHandler = async cancellationToken =>
        {
            await releaseRefresh.Task;
            cancellationToken.ThrowIfCancellationRequested();
            return CreateEntitlements(canUseAi: true);
        };

        Task retry = workspace.RetryGateLoad.ExecuteAsync();
        await HeadlessTestHelpers.WaitUntilAsync(() => workspace.IsSigningIn.Value, TimeSpan.FromSeconds(1));
        HeadlessTestHelpers.Settle();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                workspace.ShowSignInAction.Value,
                Is.False,
                "Only the retry control with its progress ring must be visible during a retry.");
            Assert.That(workspace.GateRefreshFailed.Value, Is.True);
        }

        releaseRefresh.TrySetResult();
        await retry;
        HeadlessTestHelpers.Settle();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(workspace.GateRefreshFailed.Value, Is.False);
            Assert.That(workspace.IsGateOpen.Value, Is.False);
        }
    }

    [AvaloniaTest]
    [TestCase(typeof(InvalidOperationException))]
    // An HTTP timeout surfaces as cancellation without cancelling our token.
    [TestCase(typeof(TaskCanceledException))]
    public async Task InitialLoadFailure_OpensRetryGate(Type failure)
    {
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("ai-gate-initial-failure-" + failure.Name);
        using var auth = new ReactivePropertySlim<AuthenticatedUser?>(CreateUser("user-a"));
        var entitlements = new StubEntitlementService
        {
            Failure = (Exception)Activator.CreateInstance(failure)!,
        };
        var plans = new StubPlanCoordinator();
        await using var workspace = new AiWorkspaceViewModel(
            editor,
            _ => new StubPage(),
            entitlements.Entitlements,
            auth,
            plans,
            signIn: null,
            entitlements);

        await HeadlessTestHelpers.WaitUntilAsync(() => workspace.GateRefreshFailed.Value, TimeSpan.FromSeconds(1));
        HeadlessTestHelpers.Settle();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(workspace.IsSignedIn.Value, Is.True);
            Assert.That(workspace.HasEntitlementsSnapshot.Value, Is.False);
            Assert.That(workspace.IsGateOpen.Value, Is.True);
            Assert.That(workspace.SignInError.Value, Is.EqualTo(MessageStrings.UnexpectedError));
        }

        entitlements.Failure = null;
        entitlements.RefreshResult = CreateEntitlements(canUseAi: true);
        await workspace.RetryGateLoad.ExecuteAsync();
        HeadlessTestHelpers.Settle();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(workspace.GateRefreshFailed.Value, Is.False);
            Assert.That(workspace.IsGateOpen.Value, Is.False);
        }
    }

    [AvaloniaTest]
    public async Task Dispose_CancelsInitialLoadSilently()
    {
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("ai-gate-dispose-load");
        using var auth = new ReactivePropertySlim<AuthenticatedUser?>(CreateUser("user-a"));
        var entitlements = new StubEntitlementService();
        var plans = new StubPlanCoordinator();
        var releaseRefresh = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        entitlements.RefreshHandler = async cancellationToken =>
        {
            await releaseRefresh.Task;
            cancellationToken.ThrowIfCancellationRequested();
            return CreateEntitlements(canUseAi: true);
        };
        var workspace = new AiWorkspaceViewModel(
            editor,
            _ => new StubPage(),
            entitlements.Entitlements,
            auth,
            plans,
            signIn: null,
            entitlements);
        await HeadlessTestHelpers.WaitUntilAsync(() => entitlements.RefreshCount == 1, TimeSpan.FromSeconds(1));

        await workspace.DisposeAsync();
        releaseRefresh.TrySetResult();
        HeadlessTestHelpers.Settle();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(workspace.GateRefreshFailed.Value, Is.False);
            Assert.That(workspace.SignInError.Value, Is.Null);
        }
    }

    [AvaloniaTest]
    public async Task SignInTimeout_ShowsError()
    {
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("ai-gate-sign-in-timeout");
        using var auth = new ReactivePropertySlim<AuthenticatedUser?>();
        var entitlements = new StubEntitlementService();
        var plans = new StubPlanCoordinator();
        await using var workspace = new AiWorkspaceViewModel(
            editor,
            _ => new StubPage(),
            entitlements.Entitlements,
            auth,
            plans,
            _ => Task.FromException(new TaskCanceledException("The request timed out.")),
            entitlements);

        await workspace.SignIn.ExecuteAsync();
        HeadlessTestHelpers.Settle();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(workspace.RequiresSignIn.Value, Is.True);
            Assert.That(workspace.SignInError.Value, Is.EqualTo(MessageStrings.UnexpectedError));
            Assert.That(workspace.IsSigningIn.Value, Is.False);
            Assert.That(workspace.IsGateOpen.Value, Is.True);
        }
    }

    [AvaloniaTest]
    [TestCase("cancellation")]
    [TestCase("authentication")]
    [TestCase("api")]
    public async Task RefreshFailureDuringAccountSwitch_RecoversNewAccount(string failure)
    {
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("ai-gate-account-switch-" + failure);
        using var auth = new ReactivePropertySlim<AuthenticatedUser?>(CreateUser("user-a"));
        var entitlements = new StubEntitlementService();
        var plans = new StubPlanCoordinator();
        await using var workspace = new AiWorkspaceViewModel(
            editor,
            _ => new StubPage(),
            entitlements.Entitlements,
            auth,
            plans,
            signIn: null,
            entitlements);

        // The first refresh fails after the session change: it is cancelled mid-flight, production converts
        // the superseded session's cancellation into AuthenticationRequiredException, or the old request's
        // HTTP error wins the race with the cancellation. None of them may flag the new account; the second
        // refresh serves it.
        var releaseRefresh = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int refreshCalls = 0;
        entitlements.RefreshHandler = async _ =>
        {
            await releaseRefresh.Task;
            if (Interlocked.Increment(ref refreshCalls) == 1)
            {
                throw failure switch
                {
                    "cancellation" => new OperationCanceledException(),
                    "authentication" => new AuthenticationRequiredException(),
                    _ => (Exception)await CreateApiException("{\"error_code\":\"unknown\"}"),
                };
            }
            return CreateEntitlements(canUseAi: true);
        };

        int refreshesBeforeRetry = entitlements.RefreshCount;
        Task retry = workspace.RetryGateLoad.ExecuteAsync();
        await HeadlessTestHelpers.WaitUntilAsync(() => workspace.IsSigningIn.Value, TimeSpan.FromSeconds(1));

        // Dropped by the in-flight guard; the post-flight recheck must recover it.
        auth.Value = CreateUser("user-b");
        releaseRefresh.TrySetResult();
        await retry;
        await HeadlessTestHelpers.WaitUntilAsync(
            () => workspace.HasEntitlementsSnapshot.Value, TimeSpan.FromSeconds(1));
        HeadlessTestHelpers.Settle();

        using (Assert.EnterMultipleScope())
        {
            // The retry itself plus the post-flight recheck for the new account.
            // The construction-time initial load is excluded via the baseline.
            Assert.That(
                entitlements.RefreshCount,
                Is.EqualTo(refreshesBeforeRetry + 2));
            Assert.That(workspace.GateRefreshFailed.Value, Is.False);
            Assert.That(workspace.SignInError.Value, Is.Null);
            Assert.That(workspace.IsGateOpen.Value, Is.False);
        }
    }

    private static async Task<ApiException> CreateApiException(string content)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "https://beutl.beditor.net/api/v3/user/entitlements");
        using var response = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            RequestMessage = request,
            Content = new StringContent(content, Encoding.UTF8, "application/json"),
        };
        return await ApiException.Create(request, HttpMethod.Post, response, new RefitSettings());
    }

    private static AuthenticatedUser CreateUser(string id)
    {
        BeutlApiApplication clients = TestShell.MainViewModel._beutlClients;
        var profile = new Profile(new ProfileResponse
        {
            Id = id,
            Name = id,
            DisplayName = id,
            Bio = null,
            IconId = null,
            IconUrl = null,
        }, clients);
        return new AuthenticatedUser(profile, new AuthResponse
        {
            Token = $"token-{id}",
            RefreshToken = $"refresh-{id}",
            Expiration = DateTime.UtcNow.AddHours(1),
        }, clients, DateTime.UtcNow);
    }

    private static AiEntitlements CreateEntitlements(bool canUseAi)
        => new(
            canUseAi ? "pro" : null,
            canUseAi ? "active" : null,
            null,
            null,
            false,
            canUseAi,
            new AiBalance(new AiMonthlyUsage(0, 100, false), 0, false),
            new AiOperationAvailability([]));

    private sealed class StubPage : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ModelPage : IAsyncDisposable, IAiModelListConsumer
    {
        public int RefreshModelsCount { get; private set; }

        public void RefreshModels() => RefreshModelsCount++;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class StubEntitlementService : IAiEntitlementService
    {
        private readonly ReactivePropertySlim<AiEntitlements?> _state = new();

        public IReadOnlyReactiveProperty<AiEntitlements?> Entitlements => _state;

        public int RefreshCount { get; private set; }

        public AiEntitlements? RefreshResult { get; set; }

        public Exception? Failure { get; set; }

        public Func<CancellationToken, Task<AiEntitlements?>>? RefreshHandler { get; set; }

        public void PublishSnapshot(AiEntitlements? snapshot) => _state.Value = snapshot;

        public async Task<AiEntitlements?> RefreshAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RefreshCount++;
            if (Failure is not null)
                throw Failure;
            AiEntitlements? result = RefreshHandler is null
                ? RefreshResult
                : await RefreshHandler(cancellationToken);
            _state.Value = result;
            return result;
        }
    }

    private sealed class StubPlanCoordinator : IAiPlanCoordinator
    {
        public int OpenPlanCount { get; private set; }

        public Exception? OpenPlanFailure { get; set; }

        public event EventHandler? Refreshed;

        public void RaiseRefreshed() => Refreshed?.Invoke(this, EventArgs.Empty);

        public void OpenAccountSettings()
        {
        }

        public void OpenAiPlan()
        {
            OpenPlanCount++;
            if (OpenPlanFailure is not null)
                throw OpenPlanFailure;
        }

        public Task<bool> RefreshIfPendingAsync(CancellationToken cancellationToken)
            => Task.FromResult(false);
    }
}
