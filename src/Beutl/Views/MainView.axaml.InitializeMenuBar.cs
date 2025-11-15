using System.Diagnostics.CodeAnalysis;
using System.Windows.Input;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Beutl.Api.Services;
using Beutl.Models;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.ViewModels;
using Beutl.ViewModels.Dialogs;
using Beutl.Views.Dialogs;
using FluentAvalonia.UI.Controls;
using Reactive.Bindings.Extensions;

namespace Beutl.Views;

public partial class MainView
{
    private readonly AvaloniaList<MenuItem> _rawRecentFileItems = [];
    private readonly AvaloniaList<MenuItem> _rawRecentProjItems = [];
    private readonly Cache<MenuItem> _menuItemCache = new(4);

    private void InitializeCommands(MainViewModel viewModel)
    {
        viewModel.MenuBar.CreateNewProject.Subscribe(async () =>
        {
            var dialog = new CreateNewProject();
            await dialog.ShowAsync();
        }).AddTo(_disposables);

        viewModel.MenuBar.OpenProject.Subscribe(OnOpenProject).AddTo(_disposables);
        viewModel.MenuBar.OpenFile.Subscribe(OnOpenFile).AddTo(_disposables);

        viewModel.MenuBar.RemoveFromProject.Subscribe(OnRemoveFromProject).AddTo(_disposables);

        viewModel.MenuBar.NewScene.Subscribe(async () =>
        {
            var dialog = new CreateNewScene();
            await dialog.ShowAsync();
        }).AddTo(_disposables);

        viewModel.MenuBar.AddLayer.Subscribe(OnAddElement).AddTo(_disposables);
        viewModel.MenuBar.DeleteLayer.Subscribe(OnDeleteElement).AddTo(_disposables);

        viewModel.MenuBar.Exit.Subscribe(() =>
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime applicationLifetime)
            {
                applicationLifetime.Shutdown();
            }
        }).AddTo(_disposables);
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

    private static bool TryGetSelectedEditViewModel([NotNullWhen(true)] out EditViewModel? viewModel)
    {
        if (EditorService.Current.SelectedTabItem.Value?.Context.Value is EditViewModel editViewModel)
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
            && viewModel.SelectedObject.Value is Element element)
        {
            string name = Path.GetFileName(element.Uri!.LocalPath);
            var dialog = new ContentDialog
            {
                CloseButtonText = Strings.Cancel,
                PrimaryButtonText = Strings.OK,
                DefaultButton = ContentDialogButton.Primary,
                Content = Message.DoYouWantToDeleteThisFile + "\n" + name
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                scene.RemoveChild(element).Do();
                if (File.Exists(element.Uri!.LocalPath))
                {
                    File.Delete(element.Uri!.LocalPath);
                }
            }
        }
    }

    private async void OnAddElement()
    {
        if (TryGetSelectedEditViewModel(out EditViewModel? viewModel)
            && viewModel.FindToolTab<TimelineViewModel>() is TimelineViewModel timeline)
        {
            var dialog = new AddElementDialog
            {
                DataContext = new AddElementDialogViewModel(viewModel.Scene,
                    new ElementDescription(timeline.ClickedFrame, TimeSpan.FromSeconds(5),
                        timeline.CalculateClickedLayer()),
                    viewModel.CommandRecorder)
            };
            await dialog.ShowAsync();
        }
    }

    private static async void OnRemoveFromProject(EditorTabItem? item)
    {
        Project? project = ProjectService.Current.CurrentProject.Value;
        EditorTabItem? selectedTabItem = item ?? EditorService.Current.SelectedTabItem.Value;

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
                Content = Message.DoYouWantToExcludeThisItemFromProject + "\n" + filePath
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                project.Items.Remove(projItem);
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

        filters.AddRange(ExtensionProvider.Current.GetExtensions<EditorExtension>()
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
                    MenuBarViewModel.OpenFileCore(path);
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
                        Patterns = [$"*.{Constants.ProjectFileExtension}"]
                    }
                ]
            };

            IReadOnlyList<IStorageFile> result = await window.StorageProvider.OpenFilePickerAsync(options);
            if (result.Count > 0
                && result[0].TryGetLocalPath() is string localPath)
            {
                await ProjectService.Current.OpenProject(localPath);
            }
        }
    }
}
