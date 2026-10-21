using System.Reactive.Linq;
using System.Text.Json.Nodes;
using Beutl.Api.Objects;
using Beutl.Api.Services;
using Beutl.Editor.Components.AiWorkspaceTab.Services;
using Beutl.Editor.Components.Helpers;
using Beutl.Logging;
using Beutl.Services;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;
using Refit;
using Icon = FluentIcons.Common.Icon;

namespace Beutl.Editor.Components.AiWorkspaceTab.ViewModels;

/// <summary>Identifies one page of the AI tool tab, and is what a saved dock layout remembers.</summary>
internal enum AiWorkspaceSection
{
    ImageGeneration,
    ImageEdit,
    VideoGeneration,
    Subtitles,
    Jobs,
    VideoEditing,
}

/// <summary>
/// One page of an AI tab; its view model is built on first show and kept for the tab's lifetime.
/// </summary>
internal sealed class AiWorkspaceSectionViewModel : IAsyncDisposable
{
    private readonly Func<IAsyncDisposable> _create;
    private readonly object _disposeGate = new();
    private readonly OnceAsyncDisposal _disposal;
    private IAsyncDisposable? _content;
    private bool _disposed;

    internal AiWorkspaceSectionViewModel(
        AiWorkspaceSection id,
        string displayName,
        Icon icon,
        Func<IAsyncDisposable> create)
    {
        Id = id;
        DisplayName = displayName;
        Icon = icon;
        _create = create;
        _disposal = new OnceAsyncDisposal(_disposeGate);
    }

    public AiWorkspaceSection Id { get; }

    public string DisplayName { get; }

    public Icon Icon { get; }

    internal object? Content => _content;

    internal object EnsureContent()
    {
        lock (_disposeGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _content ??= _create();
        }
    }

    public ValueTask DisposeAsync() => new(BeginDisposeAsync());

    private Task BeginDisposeAsync() => _disposal.Run(DisposeCoreAsync, () => _disposed = true);

    private async Task DisposeCoreAsync()
    {
        IAsyncDisposable? content;
        lock (_disposeGate)
        {
            content = _content;
            _content = null;
        }

        if (content is not null)
            await content.DisposeAsync();
    }
}

/// <summary>
/// An AI tool tab whose pages are the AI workflows; open a second tab to use two at once.
/// </summary>
internal sealed class AiWorkspaceViewModel : IToolContext, IAsyncDisposable
{
    private readonly CompositeDisposable _disposables = [];
    private readonly IEditorContext _editorContext;
    private readonly AiWorkspaceSectionViewModel[] _sections;
    private readonly IAiPlanCoordinator? _aiPlanCoordinator;
    private readonly Func<CancellationToken, Task>? _signIn;
    private readonly IAiEntitlementService? _entitlementService;
    private readonly LifetimeCancellationSource _lifetimeCts = new();
    private readonly ILogger _logger = Log.CreateLogger<AiWorkspaceViewModel>();
    private readonly OnceAsyncDisposal _disposal = new();
    private bool _disposed;
    private AuthenticatedUser? _gateAccount;

    internal AiWorkspaceViewModel(
        IEditorContext editorContext,
        Func<AiWorkspaceSection, IAsyncDisposable> createPage,
        IObservable<AiEntitlements?>? entitlements = null,
        IObservable<AuthenticatedUser?>? authenticatedUser = null,
        IAiPlanCoordinator? aiPlanCoordinator = null,
        Func<CancellationToken, Task>? signIn = null,
        IAiEntitlementService? entitlementService = null)
    {
        ArgumentNullException.ThrowIfNull(editorContext);
        ArgumentNullException.ThrowIfNull(createPage);

        _editorContext = editorContext;
        _aiPlanCoordinator = aiPlanCoordinator;
        _signIn = signIn;
        _entitlementService = entitlementService;

        // Making comes first and the history reads back over it, so the pages run
        // in that order rather than in the order the menu lists them.
        _sections =
        [
            Section(AiWorkspaceSection.ImageGeneration, Strings.AiImageGenerationSection, Icon.SparkleCircle),
            Section(AiWorkspaceSection.ImageEdit, Strings.AiImageEditSection, Icon.ImageEdit),
            Section(AiWorkspaceSection.VideoGeneration, Strings.AiVideoGenerationSection, Icon.Video),
            Section(AiWorkspaceSection.VideoEditing, Strings.AiVideoEditingSection, Icon.VideoClipWand),
            Section(AiWorkspaceSection.Subtitles, Strings.AiSubtitleSection, Icon.Subtitles),
            Section(AiWorkspaceSection.Jobs, Strings.AiJobCenterSection, Icon.History),
        ];

        SelectedSection = new ReactivePropertySlim<AiWorkspaceSectionViewModel?>(_sections[0])
            .DisposeWith(_disposables);

        // The selector can report null for an instant while its items are handed
        // over; keeping the last page on screen through that avoids a blank flash.
        IObservable<AiWorkspaceSectionViewModel> shown =
            SelectedSection.Where(section => section is not null).Select(section => section!);

        ActiveContent = shown
            .Select(section => section.EnsureContent())
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        // Pages are kept once built, so refresh the model list whenever one is shown again.
        ActiveContent
            .Subscribe(content => (content as IAiModelListConsumer)?.RefreshModels())
            .DisposeWith(_disposables);

        // Two AI tabs side by side are told apart by the page each one is on,
        // which is why the dock tab is named after the page rather than "AI".
        Header = shown
            .Select(section => section.DisplayName)
            .ToReadOnlyReactivePropertySlim(Strings.Ai)
            .DisposeWith(_disposables);

        // Without the production observables (headless tests build pages
        // directly) the gate stays closed so the pages remain visible.
        IObservable<AiEntitlements?>? entitlementSnapshots =
            _entitlementService?.Entitlements ?? entitlements;
        IObservable<bool> signedIn = authenticatedUser is null
            ? Observable.Return(true)
            : authenticatedUser.Select(user => user is not null);
        IObservable<bool> snapshotAvailable = entitlementSnapshots is null
            ? Observable.Return(true)
            : entitlementSnapshots.Select(snapshot => snapshot is not null);
        IObservable<bool> aiUsable = entitlementSnapshots is null
            ? Observable.Return(true)
            : entitlementSnapshots.Select(snapshot => snapshot?.CanUseAi == true);

        IsSignedIn = signedIn
            .ToReadOnlyReactivePropertySlim(true)
            .DisposeWith(_disposables);
        HasEntitlementsSnapshot = snapshotAvailable
            .ToReadOnlyReactivePropertySlim(true)
            .DisposeWith(_disposables);
        CanUseAi = aiUsable
            .ToReadOnlyReactivePropertySlim(true)
            .DisposeWith(_disposables);

        // Signed-out wins; a signed-in account without a snapshot is still loading, so the pages stay visible.
        RequiresSignIn = IsSignedIn
            .Select(signed => !signed)
            .ToReadOnlyReactivePropertySlim(false)
            .DisposeWith(_disposables);
        RequiresPlan = IsSignedIn
            .CombineLatest(HasEntitlementsSnapshot, CanUseAi,
                (signed, hasSnapshot, canUse) => signed && hasSnapshot && !canUse)
            .ToReadOnlyReactivePropertySlim(false)
            .DisposeWith(_disposables);
        IsSigningIn = new ReactivePropertySlim<bool>(false)
            .DisposeWith(_disposables);
        SignInError = new ReactivePropertySlim<string?>()
            .DisposeWith(_disposables);
        GateRefreshFailed = new ReactivePropertySlim<bool>(false)
            .DisposeWith(_disposables);
        // A new snapshot or account clears an earlier gate failure. Observe the raw user stream: switching
        // accounts does not change IsSignedIn.
        HasEntitlementsSnapshot
            .Where(hasSnapshot => hasSnapshot)
            .Subscribe(_ => ClearGateFailure())
            .DisposeWith(_disposables);
        authenticatedUser?
            .Subscribe(user =>
            {
                Volatile.Write(ref _gateAccount, user);
                ClearGateFailure();
            })
            .DisposeWith(_disposables);
        // Signing in through Account Settings, or switching accounts, clears the
        // snapshot without any page retrying its one-shot load, so pull fresh
        // entitlements here. SignInCore covers the workspace-initiated flow itself.
        authenticatedUser?
            .Skip(1)
            .Subscribe(OnAuthenticatedUserChanged)
            .DisposeWith(_disposables);
        ShowSignInAction = RequiresSignIn
            .CombineLatest(
                IsSigningIn,
                GateRefreshFailed,
                (signInRequired, signingIn, refreshFailed) => (signInRequired || signingIn) && !refreshFailed)
            .ToReadOnlyReactivePropertySlim(false)
            .DisposeWith(_disposables);
        IsGateOpen = RequiresSignIn
            .CombineLatest(RequiresPlan, (signInRequired, planRequired) => signInRequired || planRequired)
            .CombineLatest(IsSigningIn, (gateOpen, signingIn) => gateOpen || signingIn)
            .CombineLatest(GateRefreshFailed, (gateOpen, refreshFailed) => gateOpen || refreshFailed)
            .ToReadOnlyReactivePropertySlim(false)
            .DisposeWith(_disposables);

        OpenAiPlan = new ReactiveCommand()
            .DisposeWith(_disposables);
        OpenAiPlan.Subscribe(OpenAiPlanCore)
            .DisposeWith(_disposables);
        SignIn = new AsyncReactiveCommand(IsSigningIn.Select(signing => !signing))
            .WithSubscribe(SignInCore)
            .DisposeWith(_disposables);
        RetryGateLoad = new AsyncReactiveCommand(IsSigningIn.Select(signing => !signing))
            .WithSubscribe(RetryGateLoadCore)
            .DisposeWith(_disposables);

        // Without a cached snapshot, pull once so a failed initial load shows the retry gate. This intentionally
        // duplicates the first page's refresh; the service serializes the two.
        if (authenticatedUser is not null && _entitlementService is not null)
            _ = RefreshInitialGateLoadAsync();

        AiWorkspaceSectionViewModel Section(AiWorkspaceSection id, string displayName, Icon icon)
            => new(id, displayName, icon, () => createPage(id));
    }

    private void OpenAiPlanCore()
    {
        try
        {
            _aiPlanCoordinator?.OpenAiPlan();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open the AI plan page.");
        }
    }

    private async Task SignInCore()
    {
        if (_signIn is null || IsSigningIn.Value)
            return;
        AuthenticatedUser? account = Volatile.Read(ref _gateAccount);
        IsSigningIn.Value = true;
        SignInError.Value = null;
        GateRefreshFailed.Value = false;
        try
        {
            await RefreshGateAsync(account, "AI workspace sign-in failed.", timeoutOpensRetryGate: false, _signIn);
        }
        finally
        {
            ClearSigningIn();
            RefreshAfterFlightIfNeeded();
        }
    }

    private async Task RetryGateLoadCore()
    {
        if (IsSigningIn.Value)
            return;
        if (!IsSignedIn.Value)
        {
            await SignInCore();
            return;
        }

        AuthenticatedUser? account = Volatile.Read(ref _gateAccount);
        IsSigningIn.Value = true;
        SignInError.Value = null;
        try
        {
            // Cleared only on success so the retry control and its progress ring
            // stay visible for the duration of the refresh.
            await RefreshGateAsync(
                account,
                "AI workspace entitlement refresh failed.",
                timeoutOpensRetryGate: false,
                onRefreshed: () => GateRefreshFailed.Value = false);
        }
        finally
        {
            ClearSigningIn();
            RefreshAfterFlightIfNeeded();
        }
    }

    private void OnAuthenticatedUserChanged(AuthenticatedUser? user)
        => _ = RefreshOnAccountChangeAsync();

    private async Task RefreshInitialGateLoadAsync()
    {
        if (!IsSignedIn.Value || HasEntitlementsSnapshot.Value)
            return;
        AuthenticatedUser? account = Volatile.Read(ref _gateAccount);
        await RefreshGateAsync(account, "AI workspace initial entitlement load failed.", timeoutOpensRetryGate: true);
    }

    // Account changes during a flight are skipped by RefreshOnAccountChangeAsync's IsSigningIn guard, so
    // recheck after every flight.
    private void RefreshAfterFlightIfNeeded()
    {
        if (Volatile.Read(ref _disposed)
            || !IsSignedIn.Value
            || HasEntitlementsSnapshot.Value
            || GateRefreshFailed.Value)
            return;
        _ = RefreshOnAccountChangeAsync();
    }

    private async Task RefreshOnAccountChangeAsync()
    {
        if (_entitlementService is null || IsSigningIn.Value)
            return;
        AuthenticatedUser? account = Volatile.Read(ref _gateAccount);
        await RefreshGateAsync(account, "AI workspace entitlement refresh failed.", timeoutOpensRetryGate: true);
    }

    // Pulls the gate's entitlements and models for account, signing in first when signIn is given, and
    // reports a failure. A cancellation (HTTP timeouts surface as one without cancelling our token) counts
    // unless the lifetime ended or the account changed: a superseded session belongs to the post-flight
    // recheck, and SendAuthenticatedAsync turns its cancellation into AuthenticationRequiredException.
    private async Task RefreshGateAsync(
        AuthenticatedUser? account,
        string failureLog,
        bool timeoutOpensRetryGate,
        Func<CancellationToken, Task>? signIn = null,
        Action? onRefreshed = null)
    {
        try
        {
            if (signIn is not null)
            {
                await signIn(_lifetimeCts.Token);
                // The sign-in itself changes the account from null to the new user, so
                // only changes after this point count as superseding this operation.
                account = Volatile.Read(ref _gateAccount);
                // Sign-in clears the entitlement snapshot, so pull fresh entitlements before the gate closes.
            }

            await RefreshGateEntitlementsAsync();
            RefreshGateModels();
            onRefreshed?.Invoke();
        }
        catch (OperationCanceledException ex)
        {
            if (_lifetimeCts.IsCancellationRequested || IsSuperseded(account))
                return;
            if (timeoutOpensRetryGate)
            {
                PublishRefreshTimeout(ex, failureLog, account);
            }
            else
            {
                _logger.LogError(ex, failureLog);
                PublishGateFailure(MessageStrings.UnexpectedError, account);
            }
        }
        catch (AuthenticationRequiredException) when (IsSuperseded(account))
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, failureLog);
            ReportGateFailure(ex, account);
        }
    }

    // An API error is reported as one; anything else as unexpected.
    private void ReportGateFailure(Exception ex, AuthenticatedUser? account)
        => PublishGateFailure(
            ex is ApiException ? MessageStrings.ApiErrorOccurred : MessageStrings.UnexpectedError,
            account);

    private void PublishRefreshTimeout(
        OperationCanceledException ex,
        string message,
        AuthenticatedUser? account)
    {
        // Session teardown after sign-out is covered by the sign-in gate, so only a
        // signed-in account with no snapshot turns a timeout into the retry gate.
        if (!IsSignedIn.Value)
            return;
        _logger.LogError(ex, "{Message}", message);
        PublishGateFailure(MessageStrings.UnexpectedError, account);
    }

    // An account change mid-flight cancels the superseded session without touching
    // the workspace lifetime: swallowing that cancellation must not publish a
    // failure for the replacement account.
    private bool IsSuperseded(AuthenticatedUser? account)
        => !ReferenceEquals(account, Volatile.Read(ref _gateAccount));

    private async Task RefreshGateEntitlementsAsync()
    {
        if (_entitlementService is null)
            return;
        AiEntitlements? snapshot = await _entitlementService.RefreshAsync(_lifetimeCts.Token);
        // A 401 answers without throwing and publishes no snapshot: without this
        // check the gate would close onto controls of unknown eligibility.
        if (snapshot is null && IsSignedIn.Value && !HasEntitlementsSnapshot.Value)
            throw new AiException("The AI entitlement refresh returned no snapshot.");
    }

    // A page created while signed out already loaded an empty model catalog, so
    // offer the new account's models once its entitlements are known. Otherwise a
    // request could go out naming the server default instead of a chosen model.
    private void RefreshGateModels()
    {
        try
        {
            (ActiveContent.Value as IAiModelListConsumer)?.RefreshModels();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh AI models after updating entitlements.");
        }
    }

    // Called when the app returns from the plan website. A 401 there answers
    // without throwing and publishes no snapshot, so a null result must keep the
    // gate open with a retry instead of closing onto unknown eligibility.
    // Generation pages already refresh their own models on the same event, so
    // only Jobs asks the workspace to do it.
    internal void NotifyPlanReturnRefreshed(bool refreshModels)
    {
        if (IsSignedIn.Value && !HasEntitlementsSnapshot.Value)
            PublishGateFailure(MessageStrings.UnexpectedError, Volatile.Read(ref _gateAccount));
        if (refreshModels)
            RefreshGateModels();
    }

    // A failed refresh leaves a signed-in account with no snapshot, which on its
    // own would close the gate onto controls of unknown eligibility. Keep the
    // gate open with the error and a retry instead. A failure while still signed
    // out needs no flag: the sign-in gate is already open. Failures from a
    // superseded account are ignored: the post-flight recheck owns the new one.
    private void PublishGateFailure(string message, AuthenticatedUser? account)
    {
        if (Volatile.Read(ref _disposed) || IsSuperseded(account))
            return;
        try
        {
            SignInError.Value = message;
            GateRefreshFailed.Value = IsSignedIn.Value && !HasEntitlementsSnapshot.Value;
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void ClearGateFailure()
    {
        if (Volatile.Read(ref _disposed))
            return;
        try
        {
            GateRefreshFailed.Value = false;
            SignInError.Value = null;
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void ClearSigningIn()
    {
        try
        {
            if (!Volatile.Read(ref _disposed))
                IsSigningIn.Value = false;
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public ToolTabExtension Extension => AiWorkspaceTabExtension.Instance;

    public IReactiveProperty<bool> IsSelected { get; } = new ReactivePropertySlim<bool>();

    public IReadOnlyReactiveProperty<string> Header { get; }

    internal IReadOnlyList<AiWorkspaceSectionViewModel> Sections => _sections;

    internal ReactivePropertySlim<AiWorkspaceSectionViewModel?> SelectedSection { get; }

    public ReadOnlyReactivePropertySlim<object?> ActiveContent { get; }

    public ReadOnlyReactivePropertySlim<bool> IsSignedIn { get; }

    public ReadOnlyReactivePropertySlim<bool> HasEntitlementsSnapshot { get; }

    public ReadOnlyReactivePropertySlim<bool> CanUseAi { get; }

    public ReadOnlyReactivePropertySlim<bool> RequiresSignIn { get; }

    public ReadOnlyReactivePropertySlim<bool> RequiresPlan { get; }

    public ReadOnlyReactivePropertySlim<bool> IsGateOpen { get; }

    public ReactivePropertySlim<bool> IsSigningIn { get; }

    public ReactivePropertySlim<string?> SignInError { get; }

    public ReactivePropertySlim<bool> GateRefreshFailed { get; }

    public ReadOnlyReactivePropertySlim<bool> ShowSignInAction { get; }

    public ReactiveCommand OpenAiPlan { get; }

    public AsyncReactiveCommand SignIn { get; }

    public AsyncReactiveCommand RetryGateLoad { get; }

    internal IAiPlanCoordinator? AiPlanCoordinator => _aiPlanCoordinator;

    /// <summary>
    /// Brings a page to the front of this tab and hands back its view model,
    /// building it if this is the first look at that page.
    /// </summary>
    internal object Show(AiWorkspaceSection section)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        AiWorkspaceSectionViewModel target = _sections.First(item => item.Id == section);
        SelectedSection.Value = target;
        return target.EnsureContent();
    }

    public object? GetService(Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return serviceType.IsInstanceOfType(this)
            ? this
            : _editorContext.GetService(serviceType);
    }

    public void ReadFromJson(JsonObject json)
    {
        ArgumentNullException.ThrowIfNull(json);

        // Which page, and nothing from inside it: prompts and generated content
        // are deliberately kept out of the persisted dock layout.
        if (json["section"] is JsonValue value
            && value.TryGetValue(out string? name)
            && Enum.TryParse(name, out AiWorkspaceSection section))
        {
            if (Show(section) is IAiWorkspacePageState state)
                state.ReadFromJson(json);
        }
    }

    public void WriteToJson(JsonObject json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (SelectedSection.Value is { } section)
        {
            json["section"] = section.Id.ToString();
            if (section.Content is IAiWorkspacePageState state)
                state.WriteToJson(json);
        }
    }

    public ValueTask DisposeAsync() => new(BeginDisposeAsync());

    public void Dispose() => _ = BeginDisposeAsync();

    private Task BeginDisposeAsync() => _disposal.Run(DisposeCoreAsync, () => _disposed = true);

    private async Task DisposeCoreAsync()
    {
        // Cancel a pending browser sign-in or entitlement refresh first so neither
        // outlives the tab nor touches the reactive properties below after disposal.
        _lifetimeCts.Dispose();
        _disposables.Dispose();
        IsSelected.Dispose();
        Task[] disposals = _sections
            .Select(section => section.DisposeAsync().AsTask())
            .ToArray();
        await Task.WhenAll(disposals);
    }
}
