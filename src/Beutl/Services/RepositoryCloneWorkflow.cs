using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Beutl.Api;
using Beutl.Configuration;
using Beutl.Editor.Components.FileBrowserTab.Services;
using Beutl.Editor.VersionControl;
using Beutl.Logging;
using Beutl.ViewModels.Dialogs;
using Beutl.Views.Dialogs;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.Logging;

namespace Beutl.Services;

internal sealed class RepositoryCloneWorkflow(
    BeutlApiApplication api, ProjectService projects, VersionControlCoordinator versionControl, GlobalConfiguration configuration)
{
    private static readonly ILogger s_logger = Log.CreateLogger<RepositoryCloneWorkflow>();
    private readonly GitInstallationLocator _installation = new(configuration.VersionControlConfig);
    private readonly SemaphoreSlim _dialogGate = new(1, 1);

    public async Task OpenAsync(Window owner, RepositoryOpenRequest? request = null, CancellationToken cancellationToken = default)
    {
        await _dialogGate.WaitAsync(cancellationToken);
        try
        {
            if (request is not null && await TryOpenExistingProjectAsync(owner, request, cancellationToken))
                return;

            await CloneAndOpenAsync(owner, request, cancellationToken);
        }
        finally
        {
            _dialogGate.Release();
        }
    }

    private async Task<bool> TryOpenExistingProjectAsync(Window owner, RepositoryOpenRequest request, CancellationToken token)
    {
        var locator = new LocalRepositoryLocator(_installation, api);
        Project? activeProject = projects.CurrentProject.Value;
        if (activeProject?.Uri is { IsFile: true } activeUri
            && await locator.FindProjectAsync([activeUri.LocalPath], request.RepositoryId, token, request.Revision) is not null
            && ReferenceEquals(activeProject, projects.CurrentProject.Value))
        {
            owner.Activate();
            projects.Notifications.ShowInformation(Strings.Project, Strings.HostedGit_ProjectAlreadyOpen);
            return true;
        }

        string? path = await locator.FindRecentProjectAsync(configuration.ViewConfig, request.RepositoryId, token, request.Revision);
        if (path is null)
            return false;

        token.ThrowIfCancellationRequested();
        var dialog = new RepositoryCloneResultDialog
        {
            Title = Strings.HostedGit_ExistingCloneFound,
            Content = new SelectableTextBlock { Text = path, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 430 },
            PrimaryButtonText = Strings.HostedGit_OpenLocalProject,
            SecondaryButtonText = Strings.HostedGit_CloneNewCopy,
            CloseButtonText = Strings.Cancel,
            DefaultButton = FAContentDialogButton.Primary,
        };
        var result = await dialog.ShowAsync(owner);
        token.ThrowIfCancellationRequested();
        if (result == FAContentDialogResult.Primary)
        {
            if (request.Revision is not null
                && await locator.FindProjectAsync([path], request.RepositoryId, token, request.Revision) is null)
                return false;
            await projects.OpenProject(path);
        }

        return result != FAContentDialogResult.Secondary;
    }

    private async Task CloneAndOpenAsync(Window owner, RepositoryOpenRequest? request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var model = new CloneRepositoryDialogViewModel(api, configuration.ViewConfig,
            new GitRepositoryCloner(_installation), request, token);
        var dialog = new CloneRepositoryDialog { DataContext = model };
        _ = model.CheckGitAsync();
        using var registration = token.Register(() => Dispatcher.UIThread.Post(() =>
        {
            if (dialog.IsAttachedToVisualTree())
                dialog.Hide();
        }));
        if (await dialog.ShowAsync(owner) != FAContentDialogResult.Primary || model.ClonedPath is not { } destination)
            return;

        string[] files = await Task.Run(() => GitRepositoryCloner.FindProjectFiles(destination, token).ToArray(), token);
        if (files.Length == 1)
        {
            await projects.OpenProject(files[0]);
        }
        else if (files.Length > 1)
        {
            await PickProjectAsync(owner, destination, files);
        }
        else
        {
            await ShowEmptyRepositoryActionsAsync(owner, destination);
        }
    }

    private async Task PickProjectAsync(Window owner, string destination, string[] files)
    {
        var list = new ListBox
        {
            MinWidth = 280,
            MaxHeight = 260,
            ItemsSource = files.Select(file => Path.GetRelativePath(destination, file)).ToArray(),
            SelectedIndex = 0,
        };
        var dialog = new FAContentDialog
        {
            Title = Strings.HostedGit_ChooseClonedProject,
            Content = list,
            PrimaryButtonText = Strings.Open,
            CloseButtonText = Strings.Cancel,
            DefaultButton = FAContentDialogButton.Primary,
        };
        if (await dialog.ShowAsync(owner) == FAContentDialogResult.Primary
            && list.SelectedIndex >= 0 && list.SelectedIndex < files.Length)
            await projects.OpenProject(files[list.SelectedIndex]);
    }

    private async Task ShowEmptyRepositoryActionsAsync(Window owner, string destination)
    {
        var dialog = new RepositoryCloneResultDialog
        {
            Title = Strings.HostedGit_CloneRepository,
            Content = new TextBlock
            {
                Text = Strings.HostedGit_NoProjectFound,
                MaxWidth = 420,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            },
            PrimaryButtonText = Strings.CreateNew,
            SecondaryButtonText = Strings.HostedGit_OpenFolder,
            CloseButtonText = Strings.Close,
            DefaultButton = FAContentDialogButton.Primary,
        };

        switch (await dialog.ShowAsync(owner))
        {
            case FAContentDialogResult.Primary:
                await new CreateNewProject
                {
                    DataContext = new CreateNewProjectViewModel(projects, versionControl, configuration, destination)
                }.ShowAsync(owner);
                break;
            case FAContentDialogResult.Secondary:
                await FileManagerLauncher.RevealAsync(projects.Notifications, destination, isDirectory: true,
                    FileManagerLauncher.LaunchAsync, s_logger);
                break;
        }
    }
}
