using Beutl.Api;
using Beutl.Api.Objects;
using Beutl.Api.Services;
using Beutl.Editor.Components.VersionControl.ViewModels;
using Beutl.Editor.VersionControl;
using Beutl.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor.Components.VersionControlTab.ViewModels;

internal partial class VersionControlTabViewModel
{
    private string? _currentOrigin;
    private string? _currentBranch;
    private HostedGitRemoteUrl? _hostedRemote;

    internal Dictionary<(string Owner, string Name), Guid> RepositoryCreationIds { get; } = [];
    internal Func<ConnectRemoteContext, Func<GitRemoteUrl, CancellationToken, Task>, CancellationToken, Task<bool>>?
        RequestRemoteConnectionAsync
    { get; set; }

    public async Task SetRemoteAsync()
    {
        RemoteMutationLease? lease = TryAcquireRemoteMutation();
        if (lease is not null)
        {
            try
            {
                await ConfigureRemoteAsync(publish: false);
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
            if (await ConfigureRemoteAsync(publish: true))
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

    private async Task<bool> ConfigureRemoteAsync(bool publish)
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
            string? normalizedUrl = null;
            if (RequestRemoteConnectionAsync is { } showDialog)
            {
                string? expectedOrigin = (await service.GetRemotesAsync(cancellationToken)).FirstOrDefault()?.Url;
                if (!IsCurrentService(service, revision, cancellationToken))
                    return false;

                if (publish && expectedOrigin is not null)
                {
                    SetOrigin(expectedOrigin);
                    StatusMessage.Value = Strings.HostedGit_OriginAlreadyConfigured;
                    return false;
                }

                Project? project = (_editorContext.Object as IHierarchical)?.FindHierarchicalParent<Project>();
                string? projectName = project?.Name.Value;
                if (string.IsNullOrWhiteSpace(projectName))
                {
                    projectName = project?.Uri is { IsFile: true } projectUri
                        ? Path.GetFileNameWithoutExtension(projectUri.LocalPath)
                        : Path.GetFileName(service.Repository?.ProjectRoot) ?? string.Empty;
                }
                var context = new ConnectRemoteContext(projectName, expectedOrigin is not null,
                    GetRemoteUrlForPresentation(expectedOrigin), publish);
                if (!await showDialog(context, ApplyRemoteAsync, cancellationToken))
                    return false;

                async Task ApplyRemoteAsync(GitRemoteUrl url, CancellationToken token)
                {
                    token.ThrowIfCancellationRequested();
                    await coordinator.SetRemoteAsync(url, expectedOrigin, token);
                    normalizedUrl = url.Url;
                }
            }
            else
            {
                string? remoteUrl = await RequestRemoteUrlAsync(
                    HasRemote.Value ? RemoteUrl.Value : null, cancellationToken);
                if (!IsCurrentService(service, revision, cancellationToken) || string.IsNullOrWhiteSpace(remoteUrl))
                    return false;

                normalizedUrl = remoteUrl.Trim();
                await coordinator.SetRemoteAsync(normalizedUrl, cancellationToken);
            }

            if (!IsCurrentService(service, revision, cancellationToken))
                return false;

            await RefreshRemotesAsync(
                service,
                cancellationToken,
                serviceRevision: revision,
                freshness: () => IsCurrentService(service, revision, cancellationToken));
            if (!IsCurrentService(service, revision, cancellationToken))
            {
                return false;
            }

            if (normalizedUrl is not null && (!HasRemote.Value
                || RequestRemoteConnectionAsync is null && GetRemoteUrlForPresentation(normalizedUrl).Length > 0))
            {
                SetOrigin(normalizedUrl);
            }

            StatusMessage.Value = Strings.VersionControl_RemoteConnected;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or GitCredentialStorageException)
        {
            if (IsCurrentService(service, revision, cancellationToken))
            {
                _editorContext.GetRequiredService<INotificationService>().ShowError(Strings.VersionControl_ErrorTitle, ex.Message);
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
                _editorContext.GetRequiredService<INotificationService>().ShowError(
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
            (progress, cancellationToken) => _versionControlCoordinator!.PullAsync(progress, cancellationToken),
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

        SetOrigin(remote?.Url);
    }

    private readonly HostedGitRemoteLookup _hostedLookup = new();

    private void SetOrigin(string? origin)
    {
        _currentOrigin = origin;
        HasRemote.Value = origin is not null;
        RemoteUrl.Value = GetRemoteUrlForPresentation(origin);
        BeutlApiApplication? api = origin is null ? null : _editorContext.GetService<BeutlApiApplication>();
        _hostedLookup.Update(origin, api, _serviceBindingCancellation?.Token ?? CancellationToken.None, _postToUi, hosted =>
        {
            _hostedRemote = hosted;
            HostedRepositoryId.Value = hosted?.RepositoryId;
        });
    }

    internal async Task OpenOnWebAsync(string? commit = null)
    {
        if (_hostedRemote is { } remote)
            await LaunchUriAsync(remote.GetRepositoryUri(commit ?? _currentBranch));
    }

    private async Task ReconnectHostedRemoteAsync(IProjectVersionControlService expectedService, string id, AuthenticatedUser owner)
    {
        if (HostedRepositoryId.Value != id
            || !TryCaptureServiceContext(out var coordinator, out var service, out int revision, out CancellationToken token)
            || !ReferenceEquals(service, expectedService)
            || _editorContext.GetService<BeutlApiApplication>() is not { } api
            || !ReferenceEquals(api.AuthenticatedUser.Value, owner)
            || TryAcquireRemoteMutation() is not { } lease)
            return;

        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _configureRemoteCompletion = completion;
        _isConfiguringRemote.Value = true;
        string? origin = _currentOrigin;
        try
        {
            var hosted = api.GetResource<HostedGitRepositoryService>();
            var repository = await hosted.GetAsync(Guid.Parse(id), token, owner);
            await HostedGitRepositoryConnection.ConfigureAsync(hosted, owner, repository, async (remote, ct) =>
            {
                await coordinator.SetRemoteAsync(remote, origin, ct);
                return true;
            }, token);
            if (IsCurrentService(service, revision, token))
            {
                await RefreshRemotesAsync(service, token, serviceRevision: revision);
                if (IsCurrentService(service, revision, token))
                    StatusMessage.Value = Strings.HostedGit_Reconnected;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (IsCurrentService(service, revision, token))
                _editorContext.GetRequiredService<INotificationService>().ShowError(Strings.VersionControl_ErrorTitle, ex.Message);
        }
        finally
        {
            lease.Release();
            if (!_disposed)
                _isConfiguringRemote.Value = false;
            completion.TrySetResult();
        }
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
        RemoteProgress.Value = new(initialProgress);
        IsRemoteOperationRunning.Value = true;
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
                        RemoteProgress.Value = VersionControlRemoteProgress.FromOutput(value);
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
            _editorContext.GetRequiredService<INotificationService>().ShowError(Strings.VersionControl_ErrorTitle, ex.Guidance);
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
            _editorContext.GetRequiredService<INotificationService>().ShowError(
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

    private Task ShowRemoteResultNotificationAsync(RemoteOpResult result)
    {
        if (result is RemoteOpResult.AuthFailed authFailed && _service is { } service
            && HostedRepositoryId.Value is { } id
            && _editorContext.GetService<BeutlApiApplication>()?.AuthenticatedUser.Value is { } owner)
        {
            _editorContext.GetRequiredService<INotificationService>().ShowError(
                Strings.VersionControl_ErrorTitle,
                WithStderr($"{authFailed.Guidance} {Strings.HostedGit_ReconnectHint}", authFailed.Stderr),
                actions: [new NotificationAction(Strings.HostedGit_Reconnect,
                    () => _ = ReconnectHostedRemoteAsync(service, id, owner))]);
            return Task.CompletedTask;
        }

        string message = GetRemoteResultMessage(result);
        if (!string.IsNullOrWhiteSpace(message))
        {
            _editorContext.GetRequiredService<INotificationService>().ShowError(
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
