using Beutl.Editor.VersionControl;
using Beutl.Services;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor.Components.VersionControlTab.ViewModels;

internal partial class VersionControlTabViewModel
{
    public async Task SetRemoteAsync()
    {
        RemoteMutationLease? lease = TryAcquireRemoteMutation();
        if (lease is not null)
        {
            try
            {
                await ConfigureRemoteAsync(lease);
            }
            finally
            {
                lease.Release();
            }
        }
    }

    public async Task PublishBranchAsync()
    {
        RemoteMutationLease? lease = TryAcquireRemoteMutation();
        if (lease is null)
        {
            return;
        }

        TaskCompletionSource publishCompletion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _remoteOperationCompletion = publishCompletion;
        try
        {
            if (await ConfigureRemoteAsync(lease))
            {
                await RunRemoteOperationAsync(
                    (progress, cancellationToken) => _versionControlCoordinator!.PushAsync(
                        progress,
                        cancellationToken),
                    Strings.VersionControl_Pushing,
                    lease,
                    publishCompletion);
            }
        }
        finally
        {
            publishCompletion.TrySetResult();
            lease.Release();
        }
    }

    private RemoteMutationLease? TryAcquireRemoteMutation()
    {
        var lease = new RemoteMutationLease(this);
        return Interlocked.CompareExchange(ref _remoteMutationOwner, lease, null) is null
            ? lease
            : null;
    }

    private async Task<bool> ConfigureRemoteAsync(RemoteMutationLease lease)
    {
        if (!TryCaptureServiceContext(
                out IProjectVersionControlCoordinator? coordinator,
                out IProjectVersionControlService? service,
                out int revision,
                out CancellationToken cancellationToken))
        {
            return false;
        }

        // The caller's lease keeps a remote operation from running at the same time.
        Debug.Assert(!IsRemoteOperationRunning.Value);
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _configureRemoteCompletion = completion;
        _isConfiguringRemote.Value = true;
        try
        {
            string? remoteUrl = await RequestRemoteUrlAsync(
                HasRemote.Value ? RemoteUrl.Value : null,
                cancellationToken);
            if (!IsCurrentService(service, revision, cancellationToken)
                || string.IsNullOrWhiteSpace(remoteUrl))
            {
                return false;
            }

            string normalizedUrl = remoteUrl.Trim();
            await coordinator.SetRemoteAsync(normalizedUrl, cancellationToken);
            if (!IsCurrentService(service, revision, cancellationToken))
            {
                return false;
            }

            await RefreshRemotesAsync(
                service,
                cancellationToken,
                serviceRevision: revision,
                freshness: () => IsCurrentService(service, revision, cancellationToken));
            if (!IsCurrentService(service, revision, cancellationToken))
            {
                return false;
            }

            string presentedUrl = GetRemoteUrlForPresentation(normalizedUrl);
            if (!string.IsNullOrEmpty(presentedUrl))
            {
                RemoteUrl.Value = presentedUrl;
            }
            HasRemote.Value = true;
            StatusMessage.Value = Strings.VersionControl_RemoteConnected;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or GitCredentialStorageException)
        {
            if (IsCurrentService(service, revision, cancellationToken))
            {
                NotificationService.ShowError(Strings.VersionControl_ErrorTitle, ex.Message);
            }
            return false;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (ObjectDisposedException) when (!IsCurrentService(service, revision, cancellationToken))
        {
            return false;
        }
        catch (Exception ex)
        {
            if (IsCurrentService(service, revision, cancellationToken))
            {
                _logger.LogError(ex, "Failed to configure the remote.");
                NotificationService.ShowError(
                    Strings.VersionControl_ErrorTitle,
                    string.Format(Strings.VersionControl_RemoteConnectFailedFormat, ex.Message));
            }
            return false;
        }
        finally
        {
            completion.TrySetResult();
            if (!_disposed)
            {
                _isConfiguringRemote.Value = false;
            }
        }
    }

    public Task PushAsync()
    {
        return RunRemoteOperationAsync(
            (progress, cancellationToken) => _versionControlCoordinator!.PushAsync(
                progress,
                cancellationToken),
            Strings.VersionControl_Pushing,
            lease: null);
    }

    // Test seam: tests await these after starting an operation through a command.
    internal Task RemoteOperationCompletion => _remoteOperationCompletion.Task;
    internal Task ConfigureRemoteCompletion => _configureRemoteCompletion.Task;

    public Task PullAsync()
    {
        return RunRemoteOperationAsync(
            (_, cancellationToken) => _versionControlCoordinator!.PullAsync(cancellationToken),
            Strings.VersionControl_Pulling);
    }

    private async Task RefreshRemotesAsync(
        IProjectVersionControlService service,
        CancellationToken cancellationToken,
        int? statusRefreshRevision = null,
        int? serviceRevision = null,
        Func<bool>? freshness = null)
    {
        RemoteInfo? remote = (await service.GetRemotesAsync(cancellationToken))
            .FirstOrDefault();
        if (cancellationToken.IsCancellationRequested
            || !ReferenceEquals(service, _service)
            || serviceRevision is { } bindingRevision
            && bindingRevision != Volatile.Read(ref _serviceRevision)
            || statusRefreshRevision is { } revision
            && !IsCurrentStatusRefresh(service, revision, cancellationToken)
            || freshness is not null
            && !freshness())
        {
            return;
        }

        HasRemote.Value = remote is not null;
        RemoteUrl.Value = GetRemoteUrlForPresentation(remote?.Url);
    }

    private static string GetRemoteUrlForPresentation(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return url;
        }

        if (!string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            return string.Empty;
        }

        if (string.IsNullOrEmpty(uri.UserInfo))
        {
            return url;
        }

        // An SSH user name only selects the account. Over HTTP the user name can itself be an access
        // token, as GitHub and GitLab accept, so any user information there stays hidden.
        bool allowsUserName = uri.Scheme is "ssh" or "git+ssh";
        bool hasPassword = Uri.UnescapeDataString(uri.UserInfo).Contains(':');
        return allowsUserName && !hasPassword ? url : string.Empty;
    }

    private async Task RunRemoteOperationAsync(
        Func<IProgress<string>, CancellationToken, Task<RemoteOpResult>> operation,
        string initialProgress,
        RemoteMutationLease? lease = null,
        TaskCompletionSource? completionOverride = null)
    {
        if (_versionControlCoordinator is null
            || _service is null
            || _disposed)
        {
            return;
        }

        bool ownsMutation = lease is null;
        RemoteMutationLease? operationLease = lease ?? TryAcquireRemoteMutation();
        if (operationLease is null)
        {
            return;
        }

        CancellationTokenSource operationCancellation = new();
        IProjectVersionControlService operationService = _service;
        int operationRevision = _serviceRevision;
        TaskCompletionSource operationCompletion = completionOverride ?? new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (completionOverride is null)
        {
            _remoteOperationCompletion = operationCompletion;
        }
        bool operationFinished = false;
        // The lease allows one remote operation at a time, and each clears the field when it ends.
        Debug.Assert(_remoteOperationCancellation is null);
        _remoteOperationCancellation = operationCancellation;
        Volatile.Write(ref _remoteOperationUserCancellation, 0);
        IsRemoteOperationRunning.Value = true;
        RemoteProgress.Value = initialProgress;
        CancellationToken serviceBindingToken = _serviceBindingCancellation!.Token;
        bool IsCurrentOperation() =>
            !operationFinished
            && !operationCancellation.IsCancellationRequested
            && IsCurrentService(operationService, operationRevision, serviceBindingToken);
        void ReportUserCancellation()
        {
            if (Volatile.Read(ref _remoteOperationUserCancellation) != 0
                && IsCurrentService(operationService, operationRevision, serviceBindingToken))
            {
                StatusMessage.Value = Strings.VersionControl_RemoteOperationCanceled;
            }
        }
        var progress = new CallbackProgress<string>(value =>
        {
            if (IsCurrentOperation())
            {
                _postToUi(() =>
                {
                    if (IsCurrentOperation())
                    {
                        RemoteProgress.Value = value;
                    }
                });
            }
        });
        try
        {
            RemoteOpResult result = await operation(
                progress,
                operationCancellation.Token);
            if (!IsCurrentOperation())
            {
                return;
            }
            if (result is RemoteOpResult.Success)
            {
                await RefreshRemotesAsync(
                    operationService,
                    operationCancellation.Token,
                    serviceRevision: operationRevision,
                    freshness: IsCurrentOperation);
                if (!IsCurrentOperation())
                {
                    if (operationCancellation.IsCancellationRequested)
                    {
                        ReportUserCancellation();
                    }
                    return;
                }
                StatusMessage.Value = Strings.VersionControl_RemoteOperationSucceeded;
            }
            else if (result is not RemoteOpResult.Failed { Stderr.Length: 0 } && IsCurrentOperation())
            {
                await ShowRemoteResultAsync(result);
            }
        }
        catch (VersionControlConflictedException ex)
        {
            if (!IsCurrentOperation())
            {
                return;
            }
            StatusMessage.Value = ex.Guidance;
            NotificationService.ShowError(Strings.VersionControl_ErrorTitle, ex.Guidance);
        }
        catch (OperationCanceledException) when (operationCancellation.IsCancellationRequested)
        {
            ReportUserCancellation();
        }
        catch (OperationCanceledException) when (serviceBindingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (!IsCurrentOperation())
            {
                return;
            }
            // A Git failure's message already carries its credential-redacted stderr, which says
            // what went wrong.
            _logger.LogError(ex, "The remote operation command failed.");
            NotificationService.ShowError(
                Strings.VersionControl_ErrorTitle,
                string.Format(Strings.VersionControl_RemoteOperationFailedFormat, ex.Message));
        }
        finally
        {
            operationFinished = true;
            if (!_disposed)
            {
                IsRemoteOperationRunning.Value = false;
            }
            _remoteOperationCancellation = null;
            operationCancellation.Dispose();
            operationCompletion.TrySetResult();
            if (ownsMutation)
            {
                operationLease.Release();
            }
        }
    }

    private void CancelRemoteOperation()
    {
        Volatile.Write(ref _remoteOperationUserCancellation, 1);
        TryCancel(Volatile.Read(ref _remoteOperationCancellation));
    }

    private static void TryCancel(CancellationTokenSource? cancellation)
    {
        cancellation?.Cancel();
    }

    internal static string GetRemoteResultMessage(RemoteOpResult result)
    {
        return result switch
        {
            RemoteOpResult.AuthFailed authFailed => WithStderr(authFailed.Guidance, authFailed.Stderr),
            RemoteOpResult.Diverged diverged => WithStderr(Strings.VersionControl_Diverged, diverged.Stderr),
            RemoteOpResult.Offline offline => WithStderr(Strings.VersionControl_Offline, offline.Stderr),
            RemoteOpResult.RepositoryDirty => Strings.VersionControl_RepositoryDirty,
            RemoteOpResult.Failed failed => failed.Stderr,
            _ => string.Empty,
        };
    }

    // Git's own error says what failed, so the guidance never hides it.
    private static string WithStderr(string guidance, string stderr)
        => string.IsNullOrWhiteSpace(stderr) ? guidance : $"{guidance}\n{stderr.Trim()}";

    private static Task ShowRemoteResultNotificationAsync(RemoteOpResult result)
    {
        string message = GetRemoteResultMessage(result);
        if (!string.IsNullOrWhiteSpace(message))
        {
            NotificationService.ShowError(
                Strings.VersionControl_ErrorTitle,
                message);
        }

        return Task.CompletedTask;
    }

    private sealed class RemoteMutationLease(VersionControlTabViewModel owner)
    {
        private int _released;

        public void Release()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                Interlocked.CompareExchange(ref owner._remoteMutationOwner, null, this);
            }
        }
    }

    private sealed class CallbackProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value)
        {
            callback(value);
        }
    }
}
