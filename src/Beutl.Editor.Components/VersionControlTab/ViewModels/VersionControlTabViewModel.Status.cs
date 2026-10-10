using System.Diagnostics.CodeAnalysis;
using Beutl.Editor.VersionControl;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor.Components.VersionControlTab.ViewModels;

internal partial class VersionControlTabViewModel
{
    private void OnServicePublished(IProjectVersionControlService? service)
    {
        _serviceBindingCancellation?.Cancel();
        _postToUi(() =>
        {
            if (!_disposed)
            {
                Initialization = RebindServiceAsync(service);
            }
        });
    }

    private Task RebindServiceAsync(IProjectVersionControlService? service)
    {
        _serviceBindingCancellation?.Cancel();
        _serviceBindingCancellation?.Dispose();
        _serviceBindingCancellation = new CancellationTokenSource();
        int revision = ++_serviceRevision;

        DetachServiceEvents();
        _service = service;
        _lockRecoveryService = service as IRepositoryLockRecoveryService;
        if (_service is not null)
        {
            _service.StatusChanged += OnStatusChanged;
        }

        if (_lockRecoveryService is not null)
        {
            _lockRecoveryService.RecoverableLockAvailable += OnRecoverableLockAvailable;
        }

        ResetRepositoryState();
        return InitializeAsync(
            service,
            revision,
            _serviceBindingCancellation.Token);
    }

    private void DetachServiceEvents()
    {
        if (_service is not null)
        {
            _service.StatusChanged -= OnStatusChanged;
        }

        if (_lockRecoveryService is not null)
        {
            _lockRecoveryService.RecoverableLockAvailable -= OnRecoverableLockAvailable;
        }
    }

    private void ResetRepositoryState()
    {
        InvalidatePreviewCache();
        _statusRefreshCancellation?.Cancel();
        CancelSelection();
        TryCancel(Volatile.Read(ref _remoteOperationCancellation));

        DisposeAndClearHistoryItems();
        SelectedCommit.Value = null;
        SelectedFile.Value = null;
        _showingDetail.Value = false;
        _nextHistoryOffset = 0;
        _historyIdentity = null;
        _lastStatusSequence = 0;
        _lastStatusHead = null;
        _metadataRefreshFailed = false;
        _hasMoreHistory = false;
        _aheadCount = 0;
        _behindCount = 0;
        _hasUncommittedChanges = false;
        Interlocked.Increment(ref _statusRefreshRevision);

        bool isTracked = _service?.Repository is not null;
        IsTracked.Value = isTracked;
        IsGitAvailable.Value = false;
        IsUnavailable.Value = false;
        IsConflicted.Value = false;
        IsDetachedHead.Value = false;
        HasBlockingGuidance.Value = false;
        HasRecoverableLock.Value = _lockRecoveryService?.RecoverableLock is not null;
        StaleLockGuidance.Value = Strings.VersionControl_StaleLockGuidance;
        DirtySummary.Value = string.Empty;
        StatusMessage.Value = isTracked
            ? string.Empty
            : Strings.VersionControl_NoRepository;
        IsLoading.Value = false;
        HasMoreHistory.Value = isTracked;
        IsHistoryEmpty.Value = true;
        CommitMessage.Value = string.Empty;
        RemoteUrl.Value = string.Empty;
        HasRemote.Value = false;
        RemoteProgress.Value = string.Empty;
        IsNestedRepository.Value = _service?.Repository?.IsNestedInForeignRepo == true;
        RepositoryScopeText.Value = GetRepositoryScopeText(_service);
        UpdatePrimaryAction();
    }

    private async Task InitializeAsync(
        IProjectVersionControlService? service,
        int revision,
        CancellationToken cancellationToken)
    {
        if (service is null)
        {
            return;
        }

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

        IsGitAvailable.Value = availability.State == GitAvailabilityState.Installed;
        if (availability.State != GitAvailabilityState.Installed)
        {
            IsUnavailable.Value = true;
            HasBlockingGuidance.Value = true;
            IsTracked.Value = false;
            HasMoreHistory.Value = false;
            StatusMessage.Value = GetAvailabilityMessage(availability);
            return;
        }

        if (service.Repository is null)
        {
            StatusMessage.Value = Strings.VersionControl_NoRepository;
            return;
        }

        int statusRefreshRevision = Volatile.Read(ref _statusRefreshRevision);
        WorkspaceStatus status;
        try
        {
            status = await service.GetStatusAsync(cancellationToken);
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

        if (!IsCurrentStatusRefresh(service, statusRefreshRevision, cancellationToken))
        {
            // A delayed older notification must not invalidate a newer sequenced read.
            if (status.NotificationSequence <= 0 || _lastStatusSequence <= 0
                || status.NotificationSequence < _lastStatusSequence)
            {
                return;
            }

            statusRefreshRevision = Interlocked.Increment(ref _statusRefreshRevision);
            _statusRefreshCancellation?.Cancel();
        }

        ApplyStatus(status);
        try
        {
            // Remote commands stay available through a conflict, so the remotes are read even when
            // the history of the blocked worktree is not.
            await RefreshRemotesAsync(
                service,
                cancellationToken,
                statusRefreshRevision);
            if (status.HasConflicts
                || status.IsDetachedHead
                || !IsCurrentStatusRefresh(
                    service,
                    statusRefreshRevision,
                    cancellationToken))
            {
                return;
            }

            await RefreshHistoryAsync(
                service,
                status.Branch,
                statusRefreshRevision,
                cancellationToken);
            if (IsCurrentStatusRefresh(service, statusRefreshRevision, cancellationToken))
            {
                _metadataRefreshFailed = false;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
            if (IsCurrentStatusRefresh(service, statusRefreshRevision, cancellationToken))
            {
                _metadataRefreshFailed = true;
            }
            throw;
        }
    }

    private void OnRepositoryAdoptionChanged(object? sender, EventArgs e)
    {
        _postToUi(() =>
        {
            if (!_disposed)
                PendingRepositoryAdoption.Value = _repositoryAdoptionSource?.PendingRepositoryAdoption;
        });
    }

    private bool IsCurrentService(
        IProjectVersionControlService service,
        int revision,
        CancellationToken cancellationToken)
    {
        return !_disposed
               && !cancellationToken.IsCancellationRequested
               && revision == _serviceRevision
               && ReferenceEquals(service, _service);
    }

    // Captures the service with its binding revision and token, which later IsCurrentService checks
    // compare against to notice that the service was replaced meanwhile.
    private bool TryCaptureServiceContext(
        [NotNullWhen(true)] out IProjectVersionControlCoordinator? coordinator,
        [NotNullWhen(true)] out IProjectVersionControlService? service,
        out int revision,
        out CancellationToken cancellationToken)
    {
        coordinator = _versionControlCoordinator;
        service = _service;
        revision = _serviceRevision;
        cancellationToken = default;
        if (coordinator is null
            || service is null
            || _disposed)
        {
            return false;
        }

        try
        {
            cancellationToken =
                _serviceBindingCancellation?.Token ?? CancellationToken.None;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }

        return IsCurrentService(service, revision, cancellationToken);
    }

    private bool IsCurrentStatusRefresh(
        IProjectVersionControlService service,
        int revision,
        CancellationToken cancellationToken)
    {
        return !_disposed
               && !cancellationToken.IsCancellationRequested
               && revision == Volatile.Read(ref _statusRefreshRevision)
               && ReferenceEquals(service, _service);
    }

    internal static string GetAvailabilityMessage(GitAvailability availability)
    {
        ArgumentNullException.ThrowIfNull(availability);
        string stateMessage = availability.State switch
        {
            GitAvailabilityState.VersionTooOld => string.Format(
                CultureInfo.CurrentCulture,
                Strings.VersionControl_GitTooOldFormat,
                availability.Version?.ToString() ?? "—"),
            _ => Strings.VersionControl_GitNotInstalled,
        };
        string installMessage = OperatingSystem.IsWindows()
            ? Strings.VersionControl_InstallGitWindows
            : OperatingSystem.IsMacOS()
                ? Strings.VersionControl_InstallGitMacOS
                : Strings.VersionControl_InstallGitLinux;
        return $"{stateMessage}\n\n{installMessage}";
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

            if (status.NotificationSequence > 0 && status.NotificationSequence <= _lastStatusSequence)
            {
                return;
            }

            bool worktreeOnly = status.NotificationSequence > 0
                && !_metadataRefreshFailed
                && status.ChangeKind == RepositoryChangeKind.Worktree
                && status.HeadCommit is not null && status.HeadCommit == _lastStatusHead
                && _historyIdentity is { } identity && identity.Branch == status.Branch
                && !HasBlockingGuidance.Value && !status.HasConflicts && !status.IsDetachedHead;
            if (worktreeOnly)
            {
                // Do not supersede an in-flight metadata refresh with a worktree-only update.
                ApplyStatus(status);
                return;
            }

            InvalidatePreviewCache();

            int statusRefreshRevision =
                Interlocked.Increment(ref _statusRefreshRevision);
            ApplyStatus(status);
            Initialization = RunLatestStatusRefreshAsync(cancellationToken => Task.WhenAll(
                RefreshAfterStatusChangedAsync(
                    eventService,
                    status.Branch,
                    refreshHistory: !status.HasConflicts && !status.IsDetachedHead,
                    statusRefreshRevision,
                    cancellationToken),
                RefreshDisplayedPreviewAsync()));
        });
    }

    private async Task RunLatestStatusRefreshAsync(Func<CancellationToken, Task> refresh)
    {
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _serviceBindingCancellation?.Token ?? CancellationToken.None);
        CancellationTokenSource? previous = _statusRefreshCancellation;
        _statusRefreshCancellation = cancellation;
        previous?.Cancel();
        try
        {
            // Revision checks still reject late results from providers that ignore cancellation.
            // Cooperative providers also remove obsolete reads from the service operation queue.
            await refresh(cancellation.Token);
        }
        finally
        {
            if (ReferenceEquals(_statusRefreshCancellation, cancellation))
            {
                _statusRefreshCancellation = null;
            }

            // Keep the source alive until its reads finish registering cancellation callbacks.
            cancellation.Dispose();
        }
    }

    private async Task RefreshAfterStatusChangedAsync(
        IProjectVersionControlService service,
        string? branch,
        bool refreshHistory,
        int statusRefreshRevision,
        CancellationToken cancellationToken)
    {
        try
        {
            await RefreshRemotesAsync(
                service,
                cancellationToken,
                statusRefreshRevision);
            if (!refreshHistory
                || !IsCurrentStatusRefresh(
                    service,
                    statusRefreshRevision,
                    cancellationToken))
            {
                return;
            }

            await RefreshHistoryIfChangedAsync(
                service,
                branch,
                statusRefreshRevision,
                cancellationToken);
            if (IsCurrentStatusRefresh(service, statusRefreshRevision, cancellationToken))
            {
                _metadataRefreshFailed = false;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
            if (IsCurrentStatusRefresh(service, statusRefreshRevision, cancellationToken))
            {
                _metadataRefreshFailed = true;
            }
            throw;
        }
    }

    private void OnRecoverableLockAvailable(object? sender, RepositoryLockInfo lockInfo)
    {
        if (!ReferenceEquals(sender, _service))
        {
            return;
        }

        _postToUi(() =>
        {
            if (!_disposed
                && ReferenceEquals(sender, _service)
                && ReferenceEquals(_lockRecoveryService?.RecoverableLock, lockInfo))
            {
                StaleLockGuidance.Value = Strings.VersionControl_StaleLockGuidance;
                HasRecoverableLock.Value = true;
            }
        });
    }

    private void ApplyStatus(WorkspaceStatus status)
    {
        _lastStatusSequence = Math.Max(_lastStatusSequence, status.NotificationSequence);
        _lastStatusHead = status.HeadCommit;
        IsTracked.Value = _service?.Repository is not null;
        IsConflicted.Value = status.HasConflicts;
        IsDetachedHead.Value = status.IsDetachedHead;
        // A detached HEAD blocks the same writes as a conflict: there is no branch to record a
        // snapshot on until one is checked out outside Beutl.
        HasBlockingGuidance.Value = IsUnavailable.Value || status.HasConflicts || status.IsDetachedHead;
        if (status.HasConflicts)
        {
            StatusMessage.Value = Strings.VersionControl_ConflictGuidance;
            HasMoreHistory.Value = false;
        }
        else if (status.IsDetachedHead)
        {
            StatusMessage.Value = Strings.VersionControl_DetachedHeadGuidance;
            HasMoreHistory.Value = false;
        }
        else if (_historyIdentity is not null)
        {
            HasMoreHistory.Value = _hasMoreHistory;
            UpdateHistoryStatusMessage();
        }

        _aheadCount = status.Ahead;
        _behindCount = status.Behind;
        _hasUncommittedChanges = !status.IsClean;
        DirtySummary.Value = status.IsClean
            ? Strings.VersionControl_WorktreeClean
            : string.Format(
                CultureInfo.CurrentCulture,
                Strings.VersionControl_DirtySummaryFormat,
                status.Changes.Count);
        UpdatePrimaryAction();
    }
}
