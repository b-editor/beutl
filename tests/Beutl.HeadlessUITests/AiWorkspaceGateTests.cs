using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Beutl.Api;
using Beutl.Api.Clients;
using Beutl.Api.Objects;
using Beutl.Api.Services;
using Beutl.Language;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.Dialogs;
using Beutl.ViewModels.Tools;
using Beutl.Views.Tools;
using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public sealed class AiWorkspaceGateTests
{
    [AvaloniaTest]
    public async Task SignedOut_ShowsSignInGate()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-gate-signed-out");
        using var auth = new ReactivePropertySlim<AuthenticatedUser?>();
        using var entitlements = new ReactivePropertySlim<AiEntitlements?>();
        var plans = new StubPlanCoordinator();
        await using var workspace = new AiWorkspaceViewModel(
            editor, _ => new StubPage(), entitlements, auth, plans, _ => Task.CompletedTask);

        HeadlessTestHelpers.Settle();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(workspace.RequiresSignIn.Value, Is.True);
            Assert.That(workspace.RequiresPlan.Value, Is.False);
            Assert.That(workspace.IsGateOpen.Value, Is.True);
        }
    }

    [AvaloniaTest]
    public async Task SignedInWithoutSnapshot_KeepsContentVisibleWhileLoading()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-gate-loading");
        using var auth = new ReactivePropertySlim<AuthenticatedUser?>(CreateUser("user-a"));
        using var entitlements = new ReactivePropertySlim<AiEntitlements?>();
        var plans = new StubPlanCoordinator();
        await using var workspace = new AiWorkspaceViewModel(
            editor, _ => new StubPage(), entitlements, auth, plans, _ => Task.CompletedTask);

        HeadlessTestHelpers.Settle();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(workspace.RequiresSignIn.Value, Is.False);
            Assert.That(workspace.RequiresPlan.Value, Is.False);
            Assert.That(workspace.IsGateOpen.Value, Is.False);
        }
    }

    [AvaloniaTest]
    public async Task SignedInWithoutPlan_ShowsPlanGate()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-gate-no-plan");
        using var auth = new ReactivePropertySlim<AuthenticatedUser?>(CreateUser("user-a"));
        using var entitlements = new ReactivePropertySlim<AiEntitlements?>(
            CreateEntitlements(canUseAi: false));
        var plans = new StubPlanCoordinator();
        await using var workspace = new AiWorkspaceViewModel(
            editor, _ => new StubPage(), entitlements, auth, plans, _ => Task.CompletedTask);

        HeadlessTestHelpers.Settle();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(workspace.RequiresSignIn.Value, Is.False);
            Assert.That(workspace.RequiresPlan.Value, Is.True);
            Assert.That(workspace.IsGateOpen.Value, Is.True);
        }
    }

    [AvaloniaTest]
    public async Task SignedInWithPlan_ShowsContent()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-gate-with-plan");
        using var auth = new ReactivePropertySlim<AuthenticatedUser?>(CreateUser("user-a"));
        using var entitlements = new ReactivePropertySlim<AiEntitlements?>(
            CreateEntitlements(canUseAi: true));
        var plans = new StubPlanCoordinator();
        await using var workspace = new AiWorkspaceViewModel(
            editor, _ => new StubPage(), entitlements, auth, plans, _ => Task.CompletedTask);

        HeadlessTestHelpers.Settle();

        Assert.That(workspace.IsGateOpen.Value, Is.False);
    }

    [AvaloniaTest]
    public async Task SignIn_RefreshesEntitlementsAndShowsPlanGateForNonProAccount()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-gate-sign-in");
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
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-gate-sign-in-failure");
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
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-gate-refresh-failure");
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
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-gate-retry-signed-out");
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
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-gate-stale-failure");
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
        await WaitUntilAsync(() => entitlements.RefreshCount == refreshesBeforeSwitch + 1);
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
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-gate-plan-failure");
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
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-gate-model-reload");
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
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-gate-account-change");
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

        auth.Value = CreateUser("user-b");
        await WaitUntilAsync(() => entitlements.RefreshCount == 1);
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
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-gate-null-refresh");
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
    public async Task WorkspaceView_PlanReturnOnJobs_UpdatesGateWithoutModelRefresh()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-gate-plan-return");
        using var auth = new ReactivePropertySlim<AuthenticatedUser?>(CreateUser("user-a"));
        using var entitlements = new ReactivePropertySlim<AiEntitlements?>();
        var plans = new StubPlanCoordinator();
        // Jobs has no model list: the workspace callback only maintains the gate.
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
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-gate-plan-return-null");
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
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-gate-retry-progress");
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
        await WaitUntilAsync(() => workspace.IsSigningIn.Value);
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
    public async Task AccountChangeDuringFlight_RefreshesForNewAccount()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-gate-flight-change");
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

        // The first refresh is cancelled by the session change mid-flight; the
        // second one serves the new account.
        var releaseRefresh = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int refreshCalls = 0;
        entitlements.RefreshHandler = async _ =>
        {
            await releaseRefresh.Task;
            if (Interlocked.Increment(ref refreshCalls) == 1)
                throw new OperationCanceledException();
            return CreateEntitlements(canUseAi: true);
        };

        Task retry = workspace.RetryGateLoad.ExecuteAsync();
        await WaitUntilAsync(() => workspace.IsSigningIn.Value);

        // Dropped by the in-flight guard; the post-flight recheck must recover it.
        auth.Value = CreateUser("user-b");
        releaseRefresh.TrySetResult();
        await retry;
        await WaitUntilAsync(() => workspace.HasEntitlementsSnapshot.Value);
        HeadlessTestHelpers.Settle();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(entitlements.RefreshCount, Is.EqualTo(2));
            Assert.That(workspace.GateRefreshFailed.Value, Is.False);
            Assert.That(workspace.IsGateOpen.Value, Is.False);
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

    private static async Task<EditViewModel> OpenEditor(string name)
    {
        string workspace = Path.Combine(BeutlHomeIsolation.CurrentHome!, name);
        Directory.CreateDirectory(workspace);
        Project project = (await TestShell.Project.CreateProject(
            640, 480, 30, 44100, name, workspace))!;
        Scene scene = project.Items.OfType<Scene>().First();
        TestShell.Editor.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();
        return (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value!;
    }

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

#pragma warning disable CS0067 // Required by IAiPlanCoordinator; this stub never raises it.
        public event EventHandler? Refreshed;
#pragma warning restore CS0067

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
