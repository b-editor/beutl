using System.Diagnostics.CodeAnalysis;
using System.Windows.Input;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Beutl.Api.Services;
using Beutl.Configuration;
using Beutl.Editor;
using Beutl.Editor.Components.VersionControl.Views;
using Beutl.Editor.Services;
using Beutl.Editor.VersionControl;
using Beutl.Models;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.ViewModels;
using Beutl.ViewModels.Dialogs;
using Beutl.Views.Dialogs;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Reactive.Bindings.Extensions;

namespace Beutl.Views;

public partial class MainView
{
    private readonly AvaloniaList<MenuItem> _rawRecentFileItems = [];
    private readonly AvaloniaList<MenuItem> _rawRecentProjItems = [];
    private readonly Cache<MenuItem> _menuItemCache = new(4);

    private void InitializeCommands(MainViewModel viewModel)
    {
        viewModel.VersionControlCoordinator.RequestIdentityAsync = RequestGitIdentityAsync;
        viewModel.MenuBar.CreateNewProject.Subscribe(async () =>
        {
            var dialog = new CreateNewProject();
            dialog.DataContext = new CreateNewProjectViewModel(
                viewModel.ProjectService,
                viewModel.VersionControlCoordinator,
                RequestGitIdentityAsync);
            await dialog.ShowAsync();
        }).AddTo(_disposables);

        viewModel.MenuBar.OpenProject.Subscribe(OnOpenProject).AddTo(_disposables);
        viewModel.MenuBar.OpenFile.Subscribe(OnOpenFile).AddTo(_disposables);
        viewModel.MenuBar.EnableVersionControl.Subscribe(
            () => EnableVersionControlAsync(viewModel)).AddTo(_disposables);
        viewModel.MenuBar.CommitVersion.Subscribe(
            () => CommitVersionAsync(viewModel)).AddTo(_disposables);

        viewModel.MenuBar.RemoveFromProject.Subscribe(OnRemoveFromProject).AddTo(_disposables);

        viewModel.MenuBar.NewScene.Subscribe(async () =>
        {
            var dialog = new CreateNewScene();
            dialog.DataContext = new CreateNewSceneViewModel(viewModel.ProjectService, viewModel.EditorService);
            await dialog.ShowAsync();
        }).AddTo(_disposables);

        viewModel.MenuBar.DeleteLayer.Subscribe(OnDeleteElement).AddTo(_disposables);

        viewModel.MenuBar.Exit.Subscribe(() =>
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime applicationLifetime)
            {
                applicationLifetime.Shutdown();
            }
        }).AddTo(_disposables);

        viewModel.MenuBar.ExportProject.Subscribe(OnExportProject).AddTo(_disposables);
        viewModel.MenuBar.ImportProject.Subscribe(OnImportProject).AddTo(_disposables);
    }

    private async Task EnableVersionControlAsync(MainViewModel viewModel)
    {
        try
        {
            GitAvailability availability = await viewModel.VersionControlCoordinator.GetAvailabilityAsync();
            if (availability.State != GitAvailabilityState.Installed)
            {
                return;
            }

            await viewModel.VersionControlCoordinator.InitializeCurrentProjectAsync(
                RequestGitIdentityAsync);
        }
        catch (Exception ex)
        {
            await ex.Handle();
        }
    }

    private async Task<bool> RequestGitIdentityAsync(
        IProjectVersionControlService versionControlService)
    {
        var viewModel = new GitIdentityDialogViewModel(versionControlService);
        var flyout = new VersionControlPickerFlyout();
        VersionControlIdentityInput? input = await flyout.ShowIdentityAsync(
            GetVersionControlFlyoutAnchor(),
            Strings.VersionControl_IdentityTitle,
            Strings.VersionControl_IdentityName,
            Strings.VersionControl_IdentityEmail,
            viewModel.Name.Value,
            viewModel.Email.Value);
        if (input is not { } identity)
        {
            return false;
        }

        viewModel.Name.Value = identity.Name;
        viewModel.Email.Value = identity.Email;
        await viewModel.SaveAsync();
        return true;
    }

    private async Task CommitVersionAsync(MainViewModel viewModel)
    {
        var flyout = new VersionControlPickerFlyout();
        string? message = await flyout.ShowTextInputAsync(
            GetVersionControlFlyoutAnchor(),
            Strings.VersionControl_Commit,
            Strings.VersionControl_CommitMessage,
            initialText: null);
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        try
        {
            CommitResult result = await viewModel.VersionControlCoordinator.CommitManualAsync(
                message.Trim());
            NotificationService.ShowInformation(
                Strings.VersionControl,
                result is CommitResult.NoChanges
                    ? Strings.VersionControl_NothingToCommit
                    : Strings.VersionControl_CommitCreated);
        }
        catch (GitIdentityRequiredException)
        {
        }
        catch (Exception ex)
        {
            await ex.Handle();
        }
    }

    private Control GetVersionControlFlyoutAnchor()
    {
        Control? focused =
            TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Control;
        return focused is not MenuItem && focused?.IsAttachedToVisualTree() == true
            ? focused
            : this;
    }

    private void InitializeRecentItems(MainViewModel viewModel)
    {
        void AddItem(AvaloniaList<MenuItem> list, string item, ICommand command)
        {
            MenuItem menuItem = _menuItemCache.Get() ?? new MenuItem();
            menuItem.Command = command;
            menuItem.CommandParameter = item;
            menuItem.Header = item;
            list.Add(menuItem);
        }

        void RemoveItem(AvaloniaList<MenuItem> list, string item)
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                MenuItem menuItem = list[i];
                if (menuItem.Header is string header && header == item)
                {
                    list.Remove(menuItem);
                    _menuItemCache.Set(menuItem);
                }
            }
        }

        viewModel.MenuBar.RecentFileItems.ForEachItem(
                item => AddItem(_rawRecentFileItems, item, viewModel.MenuBar.OpenRecentFile),
                item => RemoveItem(_rawRecentFileItems, item),
                _rawRecentFileItems.Clear)
            .AddTo(_disposables);

        viewModel.MenuBar.RecentProjectItems.ForEachItem(
                item => AddItem(_rawRecentProjItems, item, viewModel.MenuBar.OpenRecentProject),
                item => RemoveItem(_rawRecentProjItems, item),
                _rawRecentProjItems.Clear)
            .AddTo(_disposables);
    }

    private bool TryGetSelectedEditViewModel([NotNullWhen(true)] out EditViewModel? viewModel)
    {
        if (DataContext is not MainViewModel mv) { viewModel = null; return false; }
        if (mv.EditorService.SelectedTabItem.Value?.Context.Value is EditViewModel editViewModel)
        {
            viewModel = editViewModel;
            return true;
        }
        else
        {
            viewModel = null;
            return false;
        }
    }

    private async void OnDeleteElement()
    {
        if (TryGetSelectedEditViewModel(out EditViewModel? viewModel)
            && viewModel.Scene is Scene scene
            && viewModel.GetService<IEditorSelection>()?.SelectedObject.Value is Element element)
        {
            string path = element.Uri!.LocalPath;
            string name = Path.GetFileName(path);
            var dialog = new ContentDialog
            {
                CloseButtonText = Strings.Cancel,
                PrimaryButtonText = Strings.OK,
                DefaultButton = ContentDialogButton.Primary,
                Content = MessageStrings.ConfirmDeleteFile + "\n" + name
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                viewModel.GetRequiredService<IElementStructureService>()
                    .Delete(scene, [element], GlobalConfiguration.Instance.EditorConfig.IsRippleEnabled);
            }
        }
    }

    private async void OnRemoveFromProject(EditorTabItem? item)
    {
        if (DataContext is not MainViewModel mv) return;
        Project? project = mv.ProjectService.CurrentProject.Value;
        EditorTabItem? selectedTabItem = item ?? mv.EditorService.SelectedTabItem.Value;

        if (project != null && selectedTabItem != null)
        {
            string filePath = selectedTabItem.FilePath.Value;
            ProjectItem? projItem = project.Items.FirstOrDefault(i => i == selectedTabItem.Context.Value.Object);
            if (projItem == null)
                return;

            var dialog = new ContentDialog
            {
                CloseButtonText = Strings.Cancel,
                PrimaryButtonText = Strings.OK,
                DefaultButton = ContentDialogButton.Primary,
                Content = MessageStrings.ConfirmExcludeItem + "\n" + filePath
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                try
                {
                    ProjectPersistence.RemoveItemAndPersist(project, projItem);
                }
                catch (Exception ex)
                {
                    // Surface the failed persist; RemoveItemAndPersist has already re-inserted the
                    // item.
                    await ex.Handle();
                }
            }
        }
    }

    private async void OnOpenFile()
    {
        if (VisualRoot is not Window window || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var filters = new List<FilePickerFileType>();

        filters.AddRange(viewModel.ExtensionProvider.GetExtensions<EditorExtension>()
            .Select(e => e.GetFilePickerFileType())
            .ToArray());
        var options = new FilePickerOpenOptions { AllowMultiple = true, FileTypeFilter = filters };

        IReadOnlyList<IStorageFile> files = await window.StorageProvider.OpenFilePickerAsync(options);
        if (files.Count > 0)
        {
            foreach (IStorageFile file in files)
            {
                if (file.TryGetLocalPath() is { } path)
                {
                    viewModel.MenuBar.OpenFileCore(path);
                }
            }
        }
    }

    private async void OnOpenProject()
    {
        if (VisualRoot is Window window)
        {
            var options = new FilePickerOpenOptions
            {
                FileTypeFilter =
                [
                    new FilePickerFileType(Strings.ProjectFile)
                    {
                        Patterns = [$"*.{EditorConstants.ProjectFileExtension}"]
                    }
                ]
            };

            IReadOnlyList<IStorageFile> result = await window.StorageProvider.OpenFilePickerAsync(options);
            if (result.Count > 0
                && result[0].TryGetLocalPath() is string localPath)
            {
                if (DataContext is MainViewModel mv) await mv.ProjectService.OpenProject(localPath);
            }
        }
    }

    private async Task OnExportProject()
    {
        if (VisualRoot is not Window window)
        {
            return;
        }

        if (DataContext is not MainViewModel exportVm) return;
        Project? project = exportVm.ProjectService.CurrentProject.Value;
        if (project?.Uri == null)
        {
            return;
        }

        string defaultFileName = Path.GetFileNameWithoutExtension(project.Uri.LocalPath);
        var options = new FilePickerSaveOptions
        {
            SuggestedFileName = defaultFileName,
            DefaultExtension = EditorConstants.ProjectPackageExtension,
            FileTypeChoices =
            [
                new FilePickerFileType(Strings.ProjectPackage)
                {
                    Patterns = [$"*.{EditorConstants.ProjectPackageExtension}"]
                }
            ]
        };

        IStorageFile? file = await window.StorageProvider.SaveFilePickerAsync(options);
        if (file?.TryGetLocalPath() is string outputPath)
        {
            try
            {
                ExportResult result = await ProjectPackageService.Current.ExportAsync(
                    project,
                    outputPath,
                    new Progress<(string Message, double Progress)>(p =>
                    {
                        // 進捗表示（将来的にはプログレスダイアログを表示）
                    }));

                if (!result.Success)
                {
                    _logger.LogWarning(
                        "Project export failed; partial failures collected before abort: [{Resources}]",
                        string.Join(", ", result.FailedResources));
                    NotificationService.ShowError(Strings.ExportProject, MessageStrings.OperationFailed);
                }
                else if (result.FailedResources.Count > 0)
                {
                    _logger.LogWarning(
                        "Project exported with partial failures: [{Resources}]",
                        string.Join(", ", result.FailedResources));
                    NotificationService.ShowWarning(
                        Strings.ExportProject,
                        string.Format(MessageStrings.ExportProjectPartialFailure, result.FailedResources.Count));
                }
                else
                {
                    NotificationService.ShowSuccess(Strings.ExportProject, MessageStrings.OperationCompletedSuccessfully);
                }
            }
            catch (OperationCanceledException)
            {
                // User-initiated cancellation is not a failure.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception while exporting project to {OutputPath}", outputPath);
                _ = ex.Handle();
                NotificationService.ShowError(Strings.ExportProject, MessageStrings.OperationFailed);
            }
        }
    }

    private async Task OnImportProject()
    {
        if (VisualRoot is not Window window)
        {
            return;
        }

        // パッケージファイルを選択
        var openOptions = new FilePickerOpenOptions
        {
            FileTypeFilter =
            [
                new FilePickerFileType(Strings.ProjectPackage)
                {
                    Patterns = [$"*.{EditorConstants.ProjectPackageExtension}"]
                }
            ]
        };

        IReadOnlyList<IStorageFile> files = await window.StorageProvider.OpenFilePickerAsync(openOptions);
        if (files.Count == 0 || files[0].TryGetLocalPath() is not string packagePath)
        {
            return;
        }

        // 展開先フォルダを選択
        var folderOptions = new FolderPickerOpenOptions
        {
            Title = Strings.SelectDestinationFolder
        };

        IReadOnlyList<IStorageFolder> folders = await window.StorageProvider.OpenFolderPickerAsync(folderOptions);
        if (folders.Count == 0 || folders[0].TryGetLocalPath() is not string destinationDir)
        {
            return;
        }

        try
        {
            Project? project = await ProjectPackageService.Current.ImportAsync(
                packagePath,
                destinationDir,
                new Progress<(string Message, double Progress)>(p =>
                {
                    // 進捗表示（将来的にはプログレスダイアログを表示）
                }));

            if (project?.Uri != null)
            {
                if (DataContext is MainViewModel importVm) await importVm.ProjectService.OpenProject(project.Uri.LocalPath);
                NotificationService.ShowSuccess(Strings.ImportProject, MessageStrings.OperationCompletedSuccessfully);
            }
            else
            {
                NotificationService.ShowError(Strings.ImportProject, MessageStrings.OperationFailed);
            }
        }
        catch (OperationCanceledException)
        {
            // User-initiated cancellation is not a failure.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception while importing project from {PackagePath}", packagePath);
            _ = ex.Handle();
            NotificationService.ShowError(Strings.ImportProject, MessageStrings.OperationFailed);
        }
    }
}
