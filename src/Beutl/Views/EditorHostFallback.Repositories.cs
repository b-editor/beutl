using Beutl.Api.Clients;
using Beutl.Api.Objects;
using Beutl.Editor.Components.VersionControl.ViewModels;
using Beutl.Editor.VersionControl;
using Beutl.ViewModels;
using Microsoft.Extensions.Logging;

namespace Beutl.Views;

public partial class EditorHostFallback
{
    private MainViewModel? _repositoryOwner;
    private HostedRepositoryPickerViewModel? _repositorySource;
    private IDisposable? _repositoriesSubscription;
    private CancellationTokenSource? _repositoryScan;
    private IReadOnlyList<GitRepositorySummary> _hostedRepositories = [];
    private Dictionary<string, Guid> _localRepositories = new(StringComparer.Ordinal);

    private bool IsShowingProjects => _projectsAttached && IsVisible;

    private void BindHostedProjects()
    {
        var owner = IsShowingProjects ? DataContext as MainViewModel : null;
        if (ReferenceEquals(_repositoryOwner, owner))
            return;

        _repositoriesSubscription?.Dispose();
        _repositoriesSubscription = null;
        _repositorySource?.Dispose();
        _repositorySource = null;
        _repositoryOwner = owner;
        _hostedRepositories = [];
        _localRepositories.Clear();

        if (owner is null)
        {
            RefreshHostedProjects();
            return;
        }

        var source = new HostedRepositoryPickerViewModel(owner._beutlClients, CancellationToken.None);
        _repositorySource = source;
        _repositoriesSubscription = source.Repositories.Subscribe(_ => RefreshHostedProjects());
        source.SetActive(true);
    }

    private void RefreshHostedProjects()
    {
        _repositoryScan?.Cancel();
        _repositoryScan = null;
        if (_repositoryOwner is not { } owner
            || _repositorySource?.User is not { } user
            || _repositorySource.Repositories.Value is not { Count: > 0 } repositories)
        {
            _hostedRepositories = [];
            _localRepositories.Clear();
            SyncRecentItems();
            return;
        }

        var scan = new CancellationTokenSource();
        _repositoryScan = scan;
        string[] paths = _configuration?.RecentProjects.Distinct(StringComparer.Ordinal).ToArray() ?? [];
        var locator = new LocalRepositoryLocator(owner.Configuration.VersionControlConfig);
        _ = LoadHostedProjectsAsync(locator, paths, repositories, user, scan);
    }

    private async Task LoadHostedProjectsAsync(
        LocalRepositoryLocator locator, string[] paths, IReadOnlyList<GitRepositorySummary> repositories,
        AuthenticatedUser user, CancellationTokenSource scan)
    {
        try
        {
            Dictionary<string, Guid> localRepositories = await locator.FindProjectRepositoriesAsync(paths, repositories, scan.Token);
            if (ReferenceEquals(_repositoryScan, scan) && ReferenceEquals(_repositorySource?.User, user))
            {
                _hostedRepositories = repositories;
                _localRepositories = localRepositories;
                SyncRecentItems();
            }
        }
        catch (OperationCanceledException) when (scan.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            s_logger.LogWarning(ex, "Could not match hosted repositories to recent projects.");
            if (ReferenceEquals(_repositoryScan, scan) && ReferenceEquals(_repositorySource?.User, user))
            {
                _hostedRepositories = repositories;
                _localRepositories.Clear();
                SyncRecentItems();
            }
        }
        finally
        {
            if (ReferenceEquals(_repositoryScan, scan))
                _repositoryScan = null;
            scan.Dispose();
        }
    }
}
