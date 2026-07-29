using System.Collections.ObjectModel;
using System.Globalization;
using System.Reactive.Disposables;
using Avalonia.Threading;
using Beutl.Editor.VersionControl;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.Components.VersionControl.ViewModels;

internal sealed class TitleBarBranchViewModel : IDisposable
{
    private readonly IProjectVersionControlCoordinator _coordinator;
    private readonly Action<Action> _postToUi;
    private readonly CompositeDisposable _disposables = [];
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly ObservableCollection<TitleBarBranchItemViewModel> _branches = [];
    private readonly ObservableCollection<TitleBarBranchItemViewModel> _filteredBranches = [];
    private IProjectVersionControlService? _service;
    private CancellationTokenSource? _serviceBindingCancellation;
    private int _serviceRevision;
    private bool _gitAvailable;
    private bool _coordinatorGitAvailable;
    private bool _disposed;

    internal TitleBarBranchViewModel(
        IReadOnlyReactiveProperty<IProjectVersionControlService?> serviceSource,
        IReadOnlyReactiveProperty<bool> gitAvailabilitySource,
        IProjectVersionControlCoordinator coordinator)
        : this(
            serviceSource,
            gitAvailabilitySource,
            coordinator,
            PostToUiThread)
    {
    }

    internal TitleBarBranchViewModel(
        IReadOnlyReactiveProperty<IProjectVersionControlService?> serviceSource,
        IReadOnlyReactiveProperty<bool> gitAvailabilitySource,
        IProjectVersionControlCoordinator coordinator,
        Action<Action> postToUi)
    {
        ArgumentNullException.ThrowIfNull(serviceSource);
        ArgumentNullException.ThrowIfNull(gitAvailabilitySource);
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _postToUi = postToUi ?? throw new ArgumentNullException(nameof(postToUi));
        _coordinatorGitAvailable = gitAvailabilitySource.Value;

        FilteredBranches =
            new ReadOnlyObservableCollection<TitleBarBranchItemViewModel>(
                _filteredBranches);
        IsVisible = new ReactivePropertySlim<bool>()
            .DisposeWith(_disposables);
        IsBusy = new ReactivePropertySlim<bool>()
            .DisposeWith(_disposables);
        DisplayText = new ReactivePropertySlim<string>()
            .DisposeWith(_disposables);
        CurrentBranchName = new ReactivePropertySlim<string>()
            .DisposeWith(_disposables);
        AheadBehindText = new ReactivePropertySlim<string>()
            .DisposeWith(_disposables);
        AheadCount = new ReactivePropertySlim<int>()
            .DisposeWith(_disposables);
        BehindCount = new ReactivePropertySlim<int>()
            .DisposeWith(_disposables);
        HasAhead = new ReactivePropertySlim<bool>()
            .DisposeWith(_disposables);
        HasBehind = new ReactivePropertySlim<bool>()
            .DisposeWith(_disposables);
        BranchFilter = new ReactivePropertySlim<string>(string.Empty)
            .DisposeWith(_disposables);
        HasNoMatchingBranches = new ReactivePropertySlim<bool>()
            .DisposeWith(_disposables);
        BranchFilter
            .Subscribe(_ => ApplyBranchFilter())
            .DisposeWith(_disposables);
        CreateBranchCommand = new AsyncReactiveCommand(
                IsVisible.CombineLatest(
                    IsBusy,
                    static (visible, busy) => visible && !busy))
            .WithSubscribe(CreateBranchAsync)
            .DisposeWith(_disposables);
        RequestNewBranchNameAsync = static () => Task.FromResult<string?>(null);

        Initialization = RebindServiceAsync(serviceSource.Value);
        serviceSource
            .Subscribe(OnServicePublished)
            .DisposeWith(_disposables);
        gitAvailabilitySource
            .Subscribe(OnGitAvailabilityPublished)
            .DisposeWith(_disposables);
    }

    internal ReadOnlyObservableCollection<TitleBarBranchItemViewModel> FilteredBranches { get; }

    internal ReactivePropertySlim<bool> IsVisible { get; }

    internal ReactivePropertySlim<bool> IsBusy { get; }

    internal ReactivePropertySlim<string> DisplayText { get; }

    internal ReactivePropertySlim<string> CurrentBranchName { get; }

    internal ReactivePropertySlim<string> AheadBehindText { get; }

    internal ReactivePropertySlim<int> AheadCount { get; }

    internal ReactivePropertySlim<int> BehindCount { get; }

    internal ReactivePropertySlim<bool> HasAhead { get; }

    internal ReactivePropertySlim<bool> HasBehind { get; }

    internal ReactivePropertySlim<string> BranchFilter { get; }

    internal ReactivePropertySlim<bool> HasNoMatchingBranches { get; }

    internal AsyncReactiveCommand CreateBranchCommand { get; }

    internal Func<Task<string?>> RequestNewBranchNameAsync { get; set; }

    internal Task Initialization { get; private set; }

    internal async Task PrepareFlyoutAsync(
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return;
        }

        BranchFilter.Value = string.Empty;
        await RefreshAsync(cancellationToken);
    }

    internal async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return;
        }

        IProjectVersionControlService? service = _service;
        CancellationTokenSource? bindingCancellation =
            Volatile.Read(ref _serviceBindingCancellation);
        if (service is null || bindingCancellation is null)
        {
            return;
        }

        CancellationToken bindingToken;
        try
        {
            bindingToken = bindingCancellation.Token;
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        if (bindingToken.IsCancellationRequested)
        {
            return;
        }

        using var linkedCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                bindingToken,
                cancellationToken);
        await RefreshCoreAsync(
            service,
            _serviceRevision,
            linkedCancellation.Token);
    }

    internal async Task SwitchBranchAsync(
        string branchName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branchName);
        if (_disposed
            || !IsVisible.Value
            || IsBusy.Value
            || _branches.FirstOrDefault(branch =>
                string.Equals(
                    branch.Name,
                    branchName,
                    StringComparison.Ordinal)) is not { IsCurrent: false })
        {
            return;
        }

        if (!TryGetLifetimeToken(out CancellationToken lifetimeToken))
        {
            return;
        }

        IsBusy.Value = true;
        using var operationCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                lifetimeToken);
        try
        {
            await _coordinator.SwitchBranchAsync(
                branchName,
                operationCancellation.Token);
            await RefreshAsync(operationCancellation.Token);
        }
        catch (OperationCanceledException)
            when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            if (!_disposed)
            {
                IsBusy.Value = false;
            }
        }
    }

    internal async Task CreateBranchAsync()
    {
        if (_disposed || !IsVisible.Value || IsBusy.Value)
        {
            return;
        }

        if (!TryGetLifetimeToken(out CancellationToken lifetimeToken))
        {
            return;
        }

        string? branchName = await RequestNewBranchNameAsync();
        if (_disposed
            || lifetimeToken.IsCancellationRequested
            || string.IsNullOrWhiteSpace(branchName))
        {
            return;
        }

        IsBusy.Value = true;
        using var operationCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                lifetimeToken);
        try
        {
            await _coordinator.CreateBranchAsync(
                branchName.Trim(),
                operationCancellation.Token);
            await RefreshAsync(operationCancellation.Token);
        }
        catch (OperationCanceledException)
            when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            if (!_disposed)
            {
                IsBusy.Value = false;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetimeCancellation.Cancel();
        CancellationTokenSource? bindingCancellation =
            Interlocked.Exchange(ref _serviceBindingCancellation, null);
        bindingCancellation?.Cancel();
        bindingCancellation?.Dispose();
        DetachService();
        ClearBranches();
        _disposables.Dispose();
        _lifetimeCancellation.Dispose();
    }

    internal static string FormatDisplayText(
        string branchName,
        int ahead,
        int behind,
        CultureInfo? culture = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branchName);
        culture ??= CultureInfo.CurrentCulture;

        string result = branchName;
        if (ahead > 0)
        {
            result += $" ↑{ahead.ToString(culture)}";
        }

        if (behind > 0)
        {
            result += $" ↓{behind.ToString(culture)}";
        }

        return result;
    }

    private void OnServicePublished(IProjectVersionControlService? service)
    {
        _postToUi(() =>
        {
            if (!_disposed && !ReferenceEquals(service, _service))
            {
                Initialization = RebindServiceAsync(service);
            }
        });
    }

    private async Task RebindServiceAsync(IProjectVersionControlService? service)
    {
        int revision = Interlocked.Increment(ref _serviceRevision);
        var replacementCancellation = new CancellationTokenSource();
        CancellationTokenSource? previousCancellation =
            Interlocked.Exchange(
                ref _serviceBindingCancellation,
                replacementCancellation);
        previousCancellation?.Cancel();
        previousCancellation?.Dispose();
        CancellationToken cancellationToken = replacementCancellation.Token;

        DetachService();
        _service = service;
        ResetState();
        if (service is null)
        {
            return;
        }

        service.StatusChanged += OnStatusChanged;
        await RefreshCoreAsync(service, revision, cancellationToken);
    }

    private async Task RefreshCoreAsync(
        IProjectVersionControlService service,
        int revision,
        CancellationToken cancellationToken)
    {
        GitAvailability availability;
        try
        {
            availability = await service.GetAvailabilityAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (!IsCurrentService(service, revision, cancellationToken))
        {
            return;
        }

        if (availability.State != GitAvailabilityState.Installed
            || service.Repository is null
            || !_coordinatorGitAvailable)
        {
            _postToUi(() =>
            {
                if (IsCurrentService(service, revision, cancellationToken))
                {
                    _gitAvailable =
                        availability.State == GitAvailabilityState.Installed;
                    ResetRepositoryState();
                }
            });
            return;
        }

        WorkspaceStatus status;
        IReadOnlyList<BranchInfo> branches;
        try
        {
            status = await service.GetStatusAsync(cancellationToken);
            branches = await service.GetBranchesAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (!IsCurrentService(service, revision, cancellationToken))
        {
            return;
        }

        _postToUi(() =>
        {
            if (IsCurrentService(service, revision, cancellationToken))
            {
                ApplyState(status, branches);
            }
        });
    }

    private bool IsCurrentService(
        IProjectVersionControlService service,
        int revision,
        CancellationToken cancellationToken)
    {
        return !_disposed
               && _coordinatorGitAvailable
               && !cancellationToken.IsCancellationRequested
               && revision == _serviceRevision
               && ReferenceEquals(service, _service);
    }

    private void ApplyState(
        WorkspaceStatus status,
        IReadOnlyList<BranchInfo> branches)
    {
        string branchName = status.Branch
                            ?? branches.FirstOrDefault(branch => branch.IsCurrent)?.Name
                            ?? "—";
        _gitAvailable = true;
        IsVisible.Value = true;
        ApplyBranchSummary(branchName, status.Ahead, status.Behind);

        ClearBranches();
        foreach (BranchInfo branch in branches)
        {
            _branches.Add(new TitleBarBranchItemViewModel(
                branch,
                IsBusy));
        }

        ApplyBranchFilter();
    }

    private void OnStatusChanged(object? sender, WorkspaceStatus status)
    {
        if (sender is not IProjectVersionControlService eventService
            || !ReferenceEquals(eventService, _service))
        {
            return;
        }

        _postToUi(() =>
        {
            if (_disposed || !ReferenceEquals(eventService, _service))
            {
                return;
            }

            string branchName = status.Branch ?? "—";
            bool becameVisible =
                !IsVisible.Value
                && _gitAvailable
                && _coordinatorGitAvailable
                && eventService.Repository is not null;
            IsVisible.Value =
                _gitAvailable
                && _coordinatorGitAvailable
                && eventService.Repository is not null;
            ApplyBranchSummary(branchName, status.Ahead, status.Behind);
            if (becameVisible)
            {
                _ = RefreshAsync();
            }
        });
    }

    private void OnGitAvailabilityPublished(bool available)
    {
        _postToUi(() =>
        {
            if (_disposed || available == _coordinatorGitAvailable)
            {
                return;
            }

            _coordinatorGitAvailable = available;
            if (available)
            {
                _ = RefreshAsync();
            }
            else
            {
                ResetRepositoryState();
            }
        });
    }

    private void ResetState()
    {
        _gitAvailable = false;
        ResetRepositoryState();
    }

    private void ResetRepositoryState()
    {
        IsVisible.Value = false;
        DisplayText.Value = string.Empty;
        CurrentBranchName.Value = string.Empty;
        AheadBehindText.Value = string.Empty;
        AheadCount.Value = 0;
        BehindCount.Value = 0;
        HasAhead.Value = false;
        HasBehind.Value = false;
        ClearBranches();
        BranchFilter.Value = string.Empty;
    }

    private void DetachService()
    {
        if (_service is not null)
        {
            _service.StatusChanged -= OnStatusChanged;
            _service = null;
        }
    }

    private void ClearBranches()
    {
        _filteredBranches.Clear();
        foreach (TitleBarBranchItemViewModel branch in _branches)
        {
            branch.Dispose();
        }

        _branches.Clear();
        HasNoMatchingBranches.Value = false;
    }

    private void ApplyBranchSummary(
        string branchName,
        int ahead,
        int behind)
    {
        DisplayText.Value = FormatDisplayText(branchName, ahead, behind);
        CurrentBranchName.Value = branchName;
        AheadBehindText.Value = string.Format(
            CultureInfo.CurrentCulture,
            Strings.VersionControl_AheadBehindFormat,
            ahead,
            behind);
        AheadCount.Value = ahead;
        BehindCount.Value = behind;
        HasAhead.Value = ahead > 0;
        HasBehind.Value = behind > 0;
    }

    private void ApplyBranchFilter()
    {
        _filteredBranches.Clear();
        string filter = BranchFilter.Value;
        foreach (TitleBarBranchItemViewModel branch in _branches)
        {
            if (string.IsNullOrEmpty(filter)
                || branch.Name.Contains(
                    filter,
                    StringComparison.OrdinalIgnoreCase))
            {
                _filteredBranches.Add(branch);
            }
        }

        HasNoMatchingBranches.Value =
            _branches.Count > 0 && _filteredBranches.Count == 0;
    }

    private bool TryGetLifetimeToken(out CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken = _lifetimeCancellation.Token;
            return !_disposed && !cancellationToken.IsCancellationRequested;
        }
        catch (ObjectDisposedException)
        {
            cancellationToken = default;
            return false;
        }
    }

    private static void PostToUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Post(action);
        }
    }
}

internal sealed class TitleBarBranchItemViewModel : IDisposable
{
    internal TitleBarBranchItemViewModel(
        BranchInfo branch,
        IObservable<bool> isBusy)
    {
        ArgumentNullException.ThrowIfNull(branch);
        ArgumentNullException.ThrowIfNull(isBusy);

        Name = branch.Name;
        IsCurrent = branch.IsCurrent;
        CanSwitch = isBusy
            .Select(busy => !IsCurrent && !busy)
            .ToReadOnlyReactivePropertySlim(!IsCurrent);
    }

    internal string Name { get; }

    internal bool IsCurrent { get; }

    internal ReadOnlyReactivePropertySlim<bool> CanSwitch { get; }

    public void Dispose()
    {
        CanSwitch.Dispose();
    }
}
