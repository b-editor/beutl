using Avalonia.Threading;
using Beutl.Editor.VersionControl;
using Microsoft.Extensions.Logging;

namespace Beutl.Services;

internal partial class VersionControlCoordinator
{
    private async Task<GitAvailability> GetAvailabilityCoreAsync(CancellationToken cancellationToken)
    {
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeCancellation.Token);
        // A probe that a newer one overtook leaves IsGitAvailable to the newer one.
        object probe = new();
        _latestAvailabilityProbe = probe;
        GitAvailability availability = await _installationLocator.LocateAsync(
            linkedCancellation.Token);
        linkedCancellation.Token.ThrowIfCancellationRequested();
        if (!_disposed && ReferenceEquals(_latestAvailabilityProbe, probe))
        {
            SetState(_state with
            {
                IsGitAvailable = availability.State == GitAvailabilityState.Installed,
            });
        }

        return availability;
    }

    private void StartAvailabilityRefresh()
    {
        if (!_disposed)
        {
            _ = RefreshAvailabilityAsync();
        }
    }

    private async Task RefreshAvailabilityAsync()
    {
        using RunningWork work = BeginWork();
        try
        {
            await GetAvailabilityCoreAsync(_lifetimeCancellation.Token);
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (!_disposed)
            {
                SetState(_state with { IsGitAvailable = false });
            }

            _logger.LogWarning(ex, "Failed to refresh Git availability.");
        }
    }

    private void OnVersionControlConfigChanged(object? sender, EventArgs e)
    {
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.Post(() => OnVersionControlConfigChanged(sender, e), DispatcherPriority.Normal);
            return;
        }

        if (_disposed)
        {
            return;
        }

        bool executablePathChanged = TryCaptureGitExecutablePathChange();
        if (executablePathChanged)
        {
            AdvanceProjectServiceEpoch();
        }

        CaptureUseLfsWhenAvailableChange();
        if (executablePathChanged
            && _projectService.CurrentProject.Value is not null
            && _state.OwnedService?.Repository is null)
        {
            _rediscoveryRequested = true;
        }

        StartConfigurationActivation();
        StartAvailabilityRefresh();
    }

    private bool TryCaptureGitExecutablePathChange()
    {
        string? executablePath = NormalizeGitExecutablePath(_config.GitExecutablePath);
        if (NullablePathsEqual(executablePath, _observedGitExecutablePath))
        {
            return false;
        }

        _observedGitExecutablePath = executablePath;
        return true;
    }

    private void CaptureUseLfsWhenAvailableChange()
    {
        bool useLfsWhenAvailable = _config.UseLfsWhenAvailable;
        if (useLfsWhenAvailable == _observedUseLfsWhenAvailable)
        {
            return;
        }

        _observedUseLfsWhenAvailable = useLfsWhenAvailable;
        // An activation in progress may have read the old setting for the backend it is preparing.
        if (_state.OwnedService?.Repository is not null || _activation is not null)
        {
            _repositoryHygieneConfigurationDirty = true;
        }
    }

    private void StartConfigurationActivation()
    {
        if (_configurationActivationRunning
            || _disposed
            || !(_rediscoveryRequested || _repositoryHygieneConfigurationDirty))
        {
            return;
        }

        _configurationActivationRunning = true;
        _ = RunConfigurationActivationsAsync();
    }

    // Applies the changes under the operation gate, so no operation runs between them, after any activation
    // in progress has decided how the project is tracked.
    private async Task RunConfigurationActivationsAsync()
    {
        using RunningWork work = BeginWork();
        try
        {
            while (_activation is { } activation)
            {
                await activation.Completion.WaitAsync(_lifetimeCancellation.Token);
            }

            using OperationLease operation = await BeginOperationAsync(CancellationToken.None);
            while (true)
            {
                while (_activation is { } activation)
                {
                    await activation.Completion.WaitAsync(operation.CancellationToken);
                }

                if (!await ApplyConfigurationChangeAsync(operation.CancellationToken))
                {
                    break;
                }
            }
        }
        catch (Exception ex)
            when (ex is OperationCanceledException or VersionControlLifecycleUnavailableException
                  || ex is ObjectDisposedException && _disposed)
        {
            // A close or the disposal stopped the changes. A close that leaves the project open starts
            // them again.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply a version-control configuration change.");
        }
        finally
        {
            _configurationActivationRunning = false;
        }
    }

    // Applies the current settings once and returns false when nothing is left. The flag is cleared first,
    // so a change that arrives meanwhile sets it again and the caller runs once more.
    private async Task<bool> ApplyConfigurationChangeAsync(CancellationToken cancellationToken)
    {
        IProjectVersionControlBackend? trackedService =
            _state.OwnedService is { Repository: not null } owned ? owned : null;
        // A tracked backend picks up a new executable itself; an activation applies the current LFS setting.
        bool pending = trackedService is null ? _rediscoveryRequested : _repositoryHygieneConfigurationDirty;
        _rediscoveryRequested = false;
        _repositoryHygieneConfigurationDirty = false;
        if (!pending || _disposed || _projectService.CurrentProject.Value is not { } project)
        {
            return false;
        }

        try
        {
            if (trackedService is not null)
            {
                await trackedService.EnsureRepositoryHygieneAsync(cancellationToken);
            }
            else if (ActivateProject(
                         project,
                         newProject: false,
                         preparedNewProject: null,
                         cancellationToken) is { } activation)
            {
                await activation.Completion;
            }

            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Applied again if a close stopped it and then left the project open.
            _repositoryHygieneConfigurationDirty |= trackedService is not null;
            _rediscoveryRequested |= trackedService is null;
            throw;
        }
        catch (Exception ex)
        {
            // Kept for the next run, which a later setting change, close or transition starts.
            _repositoryHygieneConfigurationDirty |= trackedService is not null;
            _logger.LogError(ex, "Failed to apply a version-control configuration change.");
            return false;
        }

        return true;
    }

    private static string? NormalizeGitExecutablePath(string? path)
        => string.IsNullOrWhiteSpace(path) ? null : path;
}
