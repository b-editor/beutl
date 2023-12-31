using System.Diagnostics.CodeAnalysis;

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

using Reactive.Bindings;
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

        viewModel.MenuBar.AddToProject.Subscribe(OnAddToProject).AddTo(_disposables);
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
        void AddItem(AvaloniaList<MenuItem> list, string item, ReactiveCommandSlim<string> command)
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
            string name = Path.GetFileName(element.FileName);
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
                if (File.Exists(element.FileName))
                {
                    File.Delete(element.FileName);
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
                    new ElementDescription(timeline.ClickedFrame, TimeSpan.FromSeconds(5), timeline.CalculateClickedLayer()))
            };
            await dialog.ShowAsync();
        }
    }

    private static async void OnRemoveFromProject()
    {
        Project? project = ProjectService.Current.CurrentProject.Value;
        EditorTabItem? selectedTabItem = EditorService.Current.SelectedTabItem.Value;

        if (project != null && selectedTabItem != null)
        {
            string filePath = selectedTabItem.FilePath.Value;
            ProjectItem? wsItem = project.Items.FirstOrDefault(i => i.FileName == filePath);
            if (wsItem == null)
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
                project.Items.Remove(wsItem);
            }
        }
    }

    private static void OnAddToProject()
    {
        Project? project = ProjectService.Current.CurrentProject.Value;
        EditorTabItem? selectedTabItem = EditorService.Current.SelectedTabItem.Value;

        if (project != null && selectedTabItem != null)
        {
            string filePath = selectedTabItem.FilePath.Value;
            if (project.Items.Any(i => i.FileName == filePath))
                return;

            if (ProjectItemContainer.Current.TryGetOrCreateItem(filePath, out ProjectItem? workspaceItem))
            {
                project.Items.Add(workspaceItem);
            }
        }
    }

    private static async Task<bool?> AskAddFileToProject(Project project, ProjectItem item)
    {
        bool? addToProject = null;
        var checkBox = new CheckBox
        {
            IsChecked = false,
            Content = Message.RememberThisChoice
        };
        var contentDialog = new ContentDialog
        {
            PrimaryButtonText = Strings.Yes,
            CloseButtonText = Strings.No,
            DefaultButton = ContentDialogButton.Primary,
            Content = new StackPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = Message.DoYouWantToAddThisItemToCurrentProject + "\n" + Path.GetFileName(item.FileName)
                    },
                    checkBox
                }
            }
        };

        ContentDialogResult result = await contentDialog.ShowAsync();
        // 選択を記憶する
        if (checkBox.IsChecked.Value)
        {
            addToProject = result == ContentDialogResult.Primary;
        }

        if (result == ContentDialogResult.Primary)
        {
            project.Items.Add(item);
            EditorService.Current.ActivateTabItem(item.FileName, TabOpenMode.FromProject);
        }

        return addToProject;
    }

    private async void OnOpenFile()
    {
        if (VisualRoot is not Window root || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var filters = new List<FilePickerFileType>();

        filters.AddRange(ExtensionProvider.Current.GetExtensions<EditorExtension>()
            .Select(e => e.GetFilePickerFileType())
            .ToArray());
        var options = new FilePickerOpenOptions
        {
            AllowMultiple = true,
            FileTypeFilter = filters
        };

        IReadOnlyList<IStorageFile> files = await root.StorageProvider.OpenFilePickerAsync(options);
        if (files.Count > 0)
        {
            bool? addToProject = null;
            Project? project = ProjectService.Current.CurrentProject.Value;

            foreach (IStorageFile file in files)
            {
                if (file.TryGetLocalPath() is string path)
                {
                    if (project != null && ProjectItemContainer.Current.TryGetOrCreateItem(path, out ProjectItem? item))
                    {
                        if (!addToProject.HasValue)
                        {
                            addToProject = await AskAddFileToProject(project, item);
                        }
                        else if (addToProject.Value)
                        {
                            project.Items.Add(item);
                            EditorService.Current.ActivateTabItem(path, TabOpenMode.FromProject);
                        }
                    }

                    EditorService.Current.ActivateTabItem(path, TabOpenMode.YourSelf);
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
                FileTypeFilter = new FilePickerFileType[]
                {
                    new FilePickerFileType(Strings.ProjectFile)
                    {
                        Patterns = new[] { $"*.{Constants.ProjectFileExtension}" }
                    }
                }
            };

            IReadOnlyList<IStorageFile> result = await window.StorageProvider.OpenFilePickerAsync(options);
            if (result.Count > 0
                && result[0].TryGetLocalPath() is string localPath)
            {
                if (ProjectService.Current.OpenProject(localPath) == null)
                {
                    NotificationService.ShowInformation("", Message.CouldNotOpenProject);
                }
            }
        }
    }
}
