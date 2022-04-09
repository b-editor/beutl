using System.Collections.Specialized;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

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

    // 開いているプロジェクトが変更された
    private void ProjectChanged(Project? @new, Project? old)
    {
        // プロジェクトが開いた
        if (@new != null)
        {
            @new.Children.CollectionChanged += Project_Children_CollectionChanged;
            foreach (Scene item in @new.Children)
            {
                AddTabItem(item);
            }
        }

        // プロジェクトが閉じた
        if (old != null)
        {
            old.Children.CollectionChanged -= Project_Children_CollectionChanged;
            foreach (Scene item in old.Children)
            {
                CloseTabItem(item);
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
                AddTabItem(item);
            }
        }
        else if (e.Action == NotifyCollectionChangedAction.Remove &&
                 e.OldItems != null)
        {
            foreach (Scene item in e.OldItems.OfType<Scene>())
            {
                CloseTabItem(item);
            }
        }
    }

    // DataContextからタブを追加
    private void AddTabItem(Scene scene)
    {
        if (tabview.Items.OfType<DraggableTabItem>()
            .Any(i => i.DataContext == scene))
        {
            // 既にタブが開かれている場合、追加しない
            return;
        }

        var view = new EditView
        {
            DataContext = new EditViewModel(scene)
        };
        var tabItem = new DraggableTabItem
        {
            Header = Path.GetFileName(scene.FileName),
            Content = view,
        };

        tabview.AddTab(tabItem);
    }

    // DataContextからタブを削除
    private void CloseTabItem(Scene scene)
    {
        foreach (DraggableTabItem item in tabview.Items.OfType<DraggableTabItem>()
            .Where(i => i.DataContext == scene)
            .ToArray())
        {
            item.Close();
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
