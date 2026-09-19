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
using Beutl.ViewModels.Tools;
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

    private sealed class StubEntitlementService : IAiEntitlementService
    {
        private readonly ReactivePropertySlim<AiEntitlements?> _state = new();

        public IReadOnlyReactiveProperty<AiEntitlements?> Entitlements => _state;

        public int RefreshCount { get; private set; }

        public AiEntitlements? RefreshResult { get; set; }

        public Task<AiEntitlements?> RefreshAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RefreshCount++;
            _state.Value = RefreshResult;
            return Task.FromResult(RefreshResult);
        }
    }

    private sealed class StubPlanCoordinator : IAiPlanCoordinator
    {
        public int OpenPlanCount { get; private set; }

        public Exception? OpenPlanFailure { get; set; }

#pragma warning disable CS0067 // Required by IAiPlanCoordinator; this stub never raises it.
        public event EventHandler? Refreshed;
#pragma warning restore CS0067

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
