using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using BeUtl.Collections;
using BeUtl.Configuration;
using BeUtl.Controls;
using BeUtl.Framework;
using BeUtl.Framework.Services;
using BeUtl.ProjectSystem;
using BeUtl.ViewModels;
using BeUtl.Views;
using BeUtl.Views.Dialogs;

using Microsoft.Extensions.DependencyInjection;

namespace BeUtl.Pages;

public sealed partial class EditPage : UserControl
{
    public EditPage()
    {
        InitializeComponent();

        IProjectService service = ServiceLocator.Current.GetRequiredService<IProjectService>();
        service.ProjectObservable.Subscribe(item => ProjectChanged(item.New, item.Old));
    }

    public bool TryGetTabItem(string file, [NotNullWhen(true)] out DraggableTabItem? result)
    {
        result = tabview.Items.OfType<DraggableTabItem>()
            .FirstOrDefault(i => i.Content is IEditor editor && editor.EdittingFile == file);

        return result != null;
    }

    public void SelectOrAddTabItem(string? file)
    {
        if (File.Exists(file))
        {
            ViewConfig viewConfig = GlobalConfiguration.Instance.ViewConfig;
            CoreList<string> recentFiles = viewConfig.RecentFiles;
            recentFiles.Remove(file);
            recentFiles.Insert(0, file);

            if (TryGetTabItem(file, out DraggableTabItem? tabItem))
            {
                tabItem.IsSelected = true;
            }
            else
            {
                EditorExtension? ext = PackageManager.Instance.ExtensionProvider.MatchEditorExtension(file);

                if (ext?.TryCreateEditor(file, out IEditor? editor) == true)
                {
                    tabItem = new DraggableTabItem
                    {
                        Header = Path.GetFileName(file),
                        Content = editor,
                    };

                    if (ext.Icon != null)
                    {
                        tabItem.Icon = new PathIcon()
                        {
                            Data = ext.Icon,
                            Width = 16,
                            Height = 16,
                        };
                    }

                    tabItem.Closing += (s, e) =>
                    {
                        if (s is DraggableTabItem { Content: IEditor editor })
                        {
                            editor.Close();
                        }
                    };

                    tabview.AddTab(tabItem);
                }
            }
        }
    }

    public bool CloseTabItem(string file)
    {
        if (TryGetTabItem(file, out DraggableTabItem? item))
        {
            item.Close();
            return true;
        }
        else
        {
            return false;
        }
    }

    // 開いているプロジェクトが変更された
    private void ProjectChanged(Project? @new, Project? old)
    {
        // プロジェクトが開いた
        if (@new != null)
        {
            @new.Children.CollectionChanged += Project_Children_CollectionChanged;
            foreach (Scene item in @new.Children)
            {
                SelectOrAddTabItem(item.FileName);
            }
        }

        // プロジェクトが閉じた
        if (old != null)
        {
            old.Children.CollectionChanged -= Project_Children_CollectionChanged;
            foreach (Scene item in old.Children)
            {
                CloseTabItem(item.FileName);
            }
        }
    }

    // Project.Childrenが変更された
    private void Project_Children_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add &&
            e.NewItems != null)
        {
            foreach (Scene item in e.NewItems.OfType<Scene>())
            {
                SelectOrAddTabItem(item.FileName);
            }
        }
        else if (e.Action == NotifyCollectionChangedAction.Remove &&
                 e.OldItems != null)
        {
            foreach (Scene item in e.OldItems.OfType<Scene>())
            {
                CloseTabItem(item.FileName);
            }
        }
    }

    // '開く'がクリックされた
    private void OpenClick(object? sender, RoutedEventArgs e)
    {
        if (this.FindAncestorOfType<MainView>().DataContext is MainViewModel vm &&
            vm.OpenFile.CanExecute())
        {
            vm.OpenFile.Execute();
        }
    }

    // '新規作成'がクリックされた
    private async void NewClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not EditPageViewModel vm) return;

        if (vm.IsProjectOpened.Value)
        {
            var dialog = new CreateNewScene();
            await dialog.ShowAsync();
        }
        else
        {
            var dialog = new CreateNewProject();
            await dialog.ShowAsync();
        }
    }

    private void AddButtonClick(object? sender, RoutedEventArgs e)
    {
        if (Resources["AddButtonFlyout"] is MenuFlyout flyout)
        {
            Button btn = tabview.FindDescendantOfType<Button>();

            flyout.ShowAt(btn);
        }
    }
}
