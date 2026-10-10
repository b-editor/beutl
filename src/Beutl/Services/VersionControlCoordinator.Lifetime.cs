using Microsoft.Extensions.Logging;

namespace Beutl.Services;

internal partial class VersionControlCoordinator
{
    private void BeginDisposal()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _pendingConfigurationActivation = null;
        PreparedNewProject? preparedNewProject = _preparedNewProject;
        _preparedNewProject = null;
        try
        {
            _lifetimeCancellation.Cancel();
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "A cancellation callback failed while disposing version control.");
        }

        if (preparedNewProject?.Service is { } preparedService)
        {
            DiscardNewProjectBackend(preparedService);
        }

        _config.ConfigurationChanged -= OnVersionControlConfigChanged;
        _projectService.Opening -= InspectProjectOpeningAsync;
        _projectService.ClosingPreparing -= PrepareProjectClosingAsync;
        _projectService.ClosingFinalizing -= NotifyProjectClosingAsync;
        _projectService.TransitionCommitted -= OnProjectChanged;
        if (ReferenceEquals(_editorService.ProjectVersionControlCoordinator, this))
        {
            _editorService.ProjectVersionControlCoordinator = null;
        }

        // Work still running may use the backend, so it is hidden now and retired once that work is over.
        SetVisibleService(null);
        DisposePublishedProperties();
        _ = CompleteDisposalAsync();
    }

    private async Task CompleteDisposalAsync()
    {
        try
        {
            await WaitForRunningWorkAsync();
            ClearProjectState();
            await WaitForRunningWorkAsync();
            _operationEpochCancellation.Dispose();
            _projectServiceEpochCancellation.Dispose();
            _lifetimeCancellation.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to complete version-control coordinator disposal.");
        }
        finally
        {
            _disposalCompletion.TrySetResult();
        }
    }
}
