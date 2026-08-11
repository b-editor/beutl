using Beutl.Api;
using Beutl.Api.Clients;
using Beutl.Api.Objects;
using Beutl.Api.Services;
using Beutl.Logging;
using Beutl.Services;
using Beutl.ViewModels.ExtensionsPages;

using Microsoft.Extensions.Logging;
using Reactive.Bindings;
using Refit;

namespace Beutl.ViewModels.SettingsPages;

public sealed class AccountSettingsPageViewModel : BasePageViewModel
{
    private readonly ILogger _logger = Log.CreateLogger<AccountSettingsPageViewModel>();
    private readonly CompositeDisposable _disposables = [];
    private readonly LifetimeCancellationSource _lifetimeCts = new();
    private readonly BeutlApiApplication _clients;
    private readonly ReactivePropertySlim<CancellationTokenSource?> _cts = new();
    private readonly IAiEntitlementService _entitlements;
    private readonly IAiPlanCoordinator _aiPlanCoordinator;
    private readonly object _entitlementsLoadGate = new();
    private CancellationTokenSource? _entitlementsLoadCts;
    private long _entitlementsLoadVersion;
    private bool _disposed;

    public AccountSettingsPageViewModel(
        BeutlApiApplication clients,
        IAiPlanCoordinator aiPlanCoordinator)
    {
        _clients = clients ?? throw new ArgumentNullException(nameof(clients));
        _aiPlanCoordinator = aiPlanCoordinator
            ?? throw new ArgumentNullException(nameof(aiPlanCoordinator));
        _entitlements = clients.GetResource<IAiEntitlementService>();
        Usage = new AiUsageViewModel(_entitlements.Entitlements).DisposeWith(_disposables);
        _clients.AuthenticatedUser
            .Subscribe(HandleAuthenticatedUserChanged)
            .DisposeWith(_disposables);

        SigningIn = _cts.Select(x => x != null)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        SignIn = new(SigningIn.Select(x => !x));
        SignInWithGoogle = new(SigningIn.Select(x => !x));
        SignInWithGitHub = new(SigningIn.Select(x => !x));

        SignIn.Subscribe(async () => await SignInCore(null))
            .DisposeWith(_disposables);
        SignInWithGoogle.Subscribe(async () => await SignInCore("Google"))
            .DisposeWith(_disposables);
        SignInWithGitHub.Subscribe(async () => await SignInCore("GitHub"))
            .DisposeWith(_disposables);

        Cancel = new(SigningIn);
        Cancel.Subscribe(() => _cts.Value!.Cancel());

        SignedIn = clients.AuthenticatedUser.Select(x => x != null)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        ProfileImage = _clients.AuthenticatedUser
            .SelectMany(x => x?.Profile?.AvatarUrl ?? Observable.ReturnThenNever<string?>(null))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        Name = _clients.AuthenticatedUser
            .Select(x => x?.Profile?.Name)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        DisplayName = _clients.AuthenticatedUser
            .SelectMany(x => x?.Profile?.DisplayName ?? Observable.ReturnThenNever<string?>(null))
            .Zip(Name, (x, y) => string.IsNullOrEmpty(x) ? y : x)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        SignOut = new ReactiveCommand(SignedIn);
        SignOut.Subscribe(() => _clients.SignOut()).DisposeWith(_disposables);

        OpenAccountSettings = new();
        OpenAccountSettings.Subscribe(_aiPlanCoordinator.OpenAccountSettings).DisposeWith(_disposables);

        OpenAiPlan = new();
        OpenAiPlan.Subscribe(_aiPlanCoordinator.OpenAiPlan).DisposeWith(_disposables);

        IObservable<AiEntitlements?> currentEntitlements = _entitlements.Entitlements;

        PlanName = currentEntitlements
            .Select(x => string.Equals(x?.Plan, "pro", StringComparison.Ordinal)
                ? SettingsStrings.AiPlan_Pro
                : SettingsStrings.AiPlan_Free)
            .ToReadOnlyReactivePropertySlim(SettingsStrings.AiPlan_Free)
            .DisposeWith(_disposables);

        SubscriptionStatus = currentEntitlements
            // A portal cancellation keeps the status on "active" until the period
            // ends, so the scheduled flag is what the user needs to see.
            .Select(x => x is { CancelAtPeriodEnd: true }
                ? SettingsStrings.AiPlan_SubscriptionStatus_CancelScheduled
                : x?.SubscriptionStatus switch
                {
                    "active" => SettingsStrings.AiPlan_SubscriptionStatus_Active,
                    "canceled" => SettingsStrings.AiPlan_SubscriptionStatus_Canceled,
                    "incomplete_expired" or null => null,
                    _ => SettingsStrings.AiPlan_SubscriptionStatus_ActionRequired,
                })
            .ToReadOnlyReactivePropertySlim<string?>()
            .DisposeWith(_disposables);

        CancelScheduledNotice = currentEntitlements
            .Select(x => x is { CancelAtPeriodEnd: true, CurrentPeriodEnd: { } periodEnd }
                ? string.Format(
                    SettingsStrings.AiPlan_CancelScheduledNotice,
                    periodEnd.ToLocalTime().ToString("d"))
                : null)
            .ToReadOnlyReactivePropertySlim<string?>()
            .DisposeWith(_disposables);

        HasCancelScheduledNotice = CancelScheduledNotice
            .Select(notice => notice is not null)
            .ToReadOnlyReactivePropertySlim(false)
            .DisposeWith(_disposables);

        AiPlanActionText = currentEntitlements
            .Select(entitlements => IsManageableSubscription(entitlements?.SubscriptionStatus)
                ? SettingsStrings.AiPlan_ManageAndTopUp
                : SettingsStrings.AiPlan_JoinPro)
            .ToReadOnlyReactivePropertySlim(SettingsStrings.AiPlan_JoinPro)
            .DisposeWith(_disposables);

        Refresh = new AsyncReactiveCommand(IsLoading.Select(x => !x));
        Refresh.Subscribe(async () =>
        {
            using (Activity? activity = Telemetry.StartActivity("AccountSettingsPage.Refresh"))
            {
                try
                {
                    IsLoading.Value = true;
                    if (_clients.AuthenticatedUser.Value is { } user)
                    {
                        using (await user.Lock.LockAsync(_lifetimeCts.Token))
                        {
                            activity?.AddEvent(new("Entered_AsyncLock"));

                            await user.RefreshAsync(_lifetimeCts.Token);
                            await user.Profile.RefreshAsync(_lifetimeCts.Token);
                        }
                    }

                    await LoadEntitlementsAsync();
                }
                catch (Exception ex)
                {
                    activity?.SetStatus(ActivityStatusCode.Error);
                    await ex.Handle();
                    _logger.LogError(ex, "An unexpected error has occurred.");
                }
                finally
                {
                    IsLoading.Value = false;
                }
            }
        });
    }

    public AsyncReactiveCommand SignIn { get; }

    public AsyncReactiveCommand SignInWithGoogle { get; }

    public AsyncReactiveCommand SignInWithGitHub { get; }

    public ReactiveCommand Cancel { get; }

    public ReadOnlyReactivePropertySlim<bool> SigningIn { get; }

    public ReactivePropertySlim<string> Error { get; } = new();

    public ReadOnlyReactivePropertySlim<bool> SignedIn { get; }

    public ReadOnlyReactivePropertySlim<string?> ProfileImage { get; }

    public ReadOnlyReactivePropertySlim<string?> Name { get; }

    public ReadOnlyReactivePropertySlim<string?> DisplayName { get; }

    public ReactiveCommand SignOut { get; }

    public ReactiveCommand OpenAccountSettings { get; }

    public ReactiveCommand OpenAiPlan { get; }

    public ReadOnlyReactivePropertySlim<string> PlanName { get; }

    public ReadOnlyReactivePropertySlim<string?> SubscriptionStatus { get; }

    public ReadOnlyReactivePropertySlim<string?> CancelScheduledNotice { get; }

    public ReadOnlyReactivePropertySlim<bool> HasCancelScheduledNotice { get; }

    internal AiUsageViewModel Usage { get; }

    public ReadOnlyReactivePropertySlim<string> AiPlanActionText { get; }

    public ReactivePropertySlim<bool> IsLoading { get; } = new();

    public AsyncReactiveCommand Refresh { get; }

    // Called when the app regains focus. The UI coordinator only reports a pending
    // reload after the plan page was opened, so returning from an unrelated
    // window costs no request.
    public void NotifyReturnedToApplication()
    {
        lock (_entitlementsLoadGate)
        {
            if (_disposed)
                return;
        }

        _ = RefreshAfterExternalPlanChangeAsync();
    }

    private async Task RefreshAfterExternalPlanChangeAsync()
    {
        try
        {
            await _aiPlanCoordinator.RefreshIfPendingAsync(_lifetimeCts.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reload AI plan information.");
        }
    }

    public ReactivePropertySlim<bool> IsAiPlanLoading { get; } = new(false);

    public ReactivePropertySlim<string?> AiPlanError { get; } = new();

    public override void Dispose()
    {
        CancellationTokenSource? entitlementLoad;
        lock (_entitlementsLoadGate)
        {
            if (_disposed)
                return;

            _disposed = true;
            _entitlementsLoadVersion++;
            entitlementLoad = _entitlementsLoadCts;
            _entitlementsLoadCts = null;
        }
        CancelEntitlementLoad(entitlementLoad);
        _lifetimeCts.Cancel();
        _disposables.Dispose();
        IsAiPlanLoading.Dispose();
        AiPlanError.Dispose();
        _lifetimeCts.Dispose();
    }

    private async Task SignInCore(string? provider = null)
    {
        using (Activity? activity = Telemetry.StartActivity("AccountSettingsPage.SignInCore"))
        {
            try
            {
                _cts.Value = new CancellationTokenSource();
                _ = provider switch
                {
                    "Google" => await _clients.SignInWithGoogleAsync(_cts.Value.Token),
                    "GitHub" => await _clients.SignInWithGitHubAsync(_cts.Value.Token),
                    _ => await _clients.SignInAsync(_cts.Value.Token),
                };
            }
            catch (ApiException apiex)
            {
                activity?.SetStatus(ActivityStatusCode.Error);
                _logger.LogError(apiex, "An unexpected error has occurred.");
                // Present API failures as a localized generic sign-in error.
                Error.Value = MessageStrings.ApiErrorOccurred;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                activity?.SetStatus(ActivityStatusCode.Error);
                _logger.LogError(ex, "An unexpected error has occurred.");
                Error.Value = MessageStrings.UnexpectedError;
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                _cts.Value = null;
            }
        }
    }

    private async Task LoadEntitlementsAsync()
    {
        var authenticatedUser = _clients.AuthenticatedUser.Value;
        if (authenticatedUser == null)
            return;

        CancellationTokenSource operationCts;
        CancellationTokenSource? previousOperation;
        long operationVersion;
        lock (_entitlementsLoadGate)
        {
            if (_disposed)
                return;

            operationCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
            previousOperation = _entitlementsLoadCts;
            _entitlementsLoadCts = operationCts;
            operationVersion = ++_entitlementsLoadVersion;
            IsAiPlanLoading.Value = true;
            AiPlanError.Value = null;
        }
        CancelEntitlementLoad(previousOperation);
        try
        {
            await _entitlements.RefreshAsync(operationCts.Token);
        }
        catch (OperationCanceledException) when (operationCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load AI plan information.");
            SetEntitlementLoadError(
                authenticatedUser,
                operationCts,
                operationVersion);
        }
        finally
        {
            lock (_entitlementsLoadGate)
            {
                if (IsCurrentEntitlementLoad(
                        authenticatedUser,
                        operationCts,
                        operationVersion))
                {
                    IsAiPlanLoading.Value = false;
                    _entitlementsLoadCts = null;
                }
            }
            operationCts.Dispose();
        }
    }

    private void HandleAuthenticatedUserChanged(AuthenticatedUser? user)
    {
        CancellationTokenSource? previousOperation;
        lock (_entitlementsLoadGate)
        {
            if (_disposed)
                return;

            _entitlementsLoadVersion++;
            previousOperation = _entitlementsLoadCts;
            _entitlementsLoadCts = null;
            IsAiPlanLoading.Value = false;
            AiPlanError.Value = null;
        }
        CancelEntitlementLoad(previousOperation);
        if (user != null)
        {
            _ = LoadEntitlementsAsync();
        }
    }

    private bool IsCurrentEntitlementLoad(
        AuthenticatedUser authenticatedUser,
        CancellationTokenSource operationCts,
        long operationVersion)
    {
        return !_disposed
            && operationVersion == _entitlementsLoadVersion
            && ReferenceEquals(_entitlementsLoadCts, operationCts)
            && ReferenceEquals(_clients.AuthenticatedUser.Value, authenticatedUser);
    }

    private void SetEntitlementLoadError(
        AuthenticatedUser authenticatedUser,
        CancellationTokenSource operationCts,
        long operationVersion)
    {
        lock (_entitlementsLoadGate)
        {
            if (IsCurrentEntitlementLoad(authenticatedUser, operationCts, operationVersion))
            {
                AiPlanError.Value = SettingsStrings.AiPlan_LoadFailed;
            }
        }
    }

    private static void CancelEntitlementLoad(CancellationTokenSource? cancellationTokenSource)
    {
        try
        {
            cancellationTokenSource?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    internal static bool IsManageableSubscription(string? status)
    {
        return status is not null and not "canceled" and not "incomplete_expired";
    }
}
