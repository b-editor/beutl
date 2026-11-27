using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Beutl.Api.Clients;
using Beutl.Api.Objects;
using Beutl.Api.Services;
using Beutl.Editor.Components.VersionControl.ViewModels;
using Beutl.Editor.VersionControl;
using Beutl.Services;
using Beutl.ViewModels;
using FluentAvalonia.UI.Controls;
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
    private readonly SemaphoreSlim _remoteThumbnailGate = new(4, 4);
    private bool _deletingHostedRepository;

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
            || _repositorySource is not { RepositoryListUser: { } user } source
            || !ReferenceEquals(source.User, user)
            || source.Repositories.Value is not { Count: > 0 } repositories)
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

    private async void DeleteHostedRepository_Click(object? sender, RoutedEventArgs e)
    {
        if (_deletingHostedRepository
            || sender is not FAMenuFlyoutItem { DataContext: RecentProjectItem item }
            || _repositorySource is not { RepositoryListUser: { } user } source
            || !ReferenceEquals(source.User, user)
            || TopLevel.GetTopLevel(this) is not { } owner)
        {
            return;
        }

        Guid repositoryId;
        if (item.UnclonedRepositoryId is { } remoteId)
            repositoryId = remoteId;
        else if (!_localRepositories.TryGetValue(item.FullName, out repositoryId))
            return;

        string id = repositoryId.ToString("D");
        var repository = source.Repositories.Value.FirstOrDefault(repo => string.Equals(repo.Id, id, StringComparison.OrdinalIgnoreCase));
        if (repository is null)
            return;

        var notifications = Notifications;
        _deletingHostedRepository = true;
        try
        {
            await ShowHostedRepositoryDeletionDialogAsync(owner, repository.Name, () =>
                ReferenceEquals(_repositorySource, source) ? source.DeleteAsync(repositoryId, user) : Task.CompletedTask);
        }
        catch (OperationCanceledException)
        {
        }
        catch (HostedGitApiException ex)
        {
            notifications.ShowError(Strings.HostedGit_DeleteRepository, ex.Message);
        }
        catch (Exception ex)
        {
            s_logger.LogWarning(ex, "Could not delete hosted repository {RepositoryId}.", repositoryId);
            notifications.ShowError(Strings.HostedGit_DeleteRepository, Strings.HostedGit_RequestFailed);
        }
        finally
        {
            _deletingHostedRepository = false;
        }
    }

    internal static async Task ShowHostedRepositoryDeletionDialogAsync(
        TopLevel owner, string repositoryName, Func<Task> deleteAsync)
    {
        var ring = new FAProgressRing { Width = 20, Height = 20, IsActive = false };
        var progress = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            IsVisible = false,
            Children =
            {
                ring,
                new TextBlock { Text = Strings.HostedGit_DeletingRepository, VerticalAlignment = VerticalAlignment.Center },
            },
        };
        var dialog = new FAContentDialog
        {
            Title = Strings.HostedGit_DeleteRepository,
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new SelectableTextBlock
                    {
                        Text = repositoryName,
                        FontWeight = FontWeight.SemiBold,
                        TextWrapping = TextWrapping.Wrap,
                    },
                    new TextBlock { Text = MessageStrings.ConfirmDeleteHostedRepository, TextWrapping = TextWrapping.Wrap },
                    progress,
                },
            },
            PrimaryButtonText = Strings.Delete,
            CloseButtonText = Strings.Cancel,
            DefaultButton = FAContentDialogButton.Close,
        };

        Task? deletion = null;
        dialog.PrimaryButtonClick += (_, args) => deletion = DeleteWithProgressAsync(args);
        await dialog.ShowAsync(owner);
        if (deletion is not null)
            await deletion;

        async Task DeleteWithProgressAsync(FAContentDialogButtonClickEventArgs args)
        {
            var deferral = args.GetDeferral();
            try
            {
                dialog.IsEnabled = false;
                progress.IsVisible = true;
                ring.IsActive = true;
                await deleteAsync();
            }
            finally
            {
                deferral.Complete();
            }
        }
    }
}
