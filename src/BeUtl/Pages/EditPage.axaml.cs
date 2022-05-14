using System.Collections.Specialized;

using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using BeUtl.Collections;
using BeUtl.Framework;
using BeUtl.Services;
using BeUtl.ViewModels;
using BeUtl.Views;
using BeUtl.Views.Dialogs;

using FAPathIconSource = FluentAvalonia.UI.Controls.PathIconSource;
using FATabView = FluentAvalonia.UI.Controls.TabView;
using FATabViewItem = FluentAvalonia.UI.Controls.TabViewItem;

namespace BeUtl.Pages;

public sealed partial class EditPage : UserControl
{
    private readonly AvaloniaList<FATabViewItem> _tabItems = new();
    private IDisposable? _disposable0;

    public EditPage()
    {
        InitializeComponent();

        tabview.TabItems = _tabItems;
        tabview.SelectionChanged += TabView_SelectionChanged;
        _tabItems.CollectionChanged += TabItems_CollectionChanged;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        _disposable0?.Dispose();
        _tabItems.Clear();
        if (DataContext is EditPageViewModel viewModel)
        {
            _disposable0 = viewModel.TabItems.ForEachItem(
                (item) =>
                {
                    EditorExtension ext = item.Extension.Value;
                    if (ext.TryCreateEditor(item.FilePath.Value, out IEditor? editor))
                    {
                        editor.DataContext = item.Context.Value;
                        var tabItem = new FATabViewItem
                        {
                            [!FATabViewItem.HeaderProperty] = new Binding("FileName.Value"),
                            [!ListBoxItem.IsSelectedProperty] = new Binding("IsSelected.Value", BindingMode.TwoWay),
                            DataContext = item,
                            Content = editor
                        };

                        if (ext.Icon != null)
                        {
                            tabItem.IconSource = new FAPathIconSource()
                            {
                                Data = ext.Icon,
                            };
                        }

                        tabItem.CloseRequested += (s, _) =>
                        {
                            if (s is FATabViewItem { DataContext: EditorTabItem itemViewModel } && DataContext is EditPageViewModel viewModel)
                            {
                                viewModel.CloseTabItem(itemViewModel.FilePath.Value, itemViewModel.TabOpenMode);
                            }
                        };

                        if (item.Order < 0 || item.Order > _tabItems.Count)
                        {
                            item.Order = _tabItems.Count;
                        }

                        _tabItems.Insert(item.Order, tabItem);
                    }
                },
                (item) =>
                {
                    for (int i = 0; i < _tabItems.Count; i++)
                    {
                        FATabViewItem tabItem = _tabItems[i];
                        if (tabItem.DataContext is EditorTabItem itemViewModel
                            && itemViewModel.FilePath.Value == item.FilePath.Value)
                        {
                            itemViewModel.Order = -1;
                            _tabItems.RemoveAt(i);
                            return;
                        }
                    }
                },
                () => throw new Exception());
        }
    }

    private void TabView_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is EditPageViewModel viewModel)
        {
            if (tabview.SelectedItem is FATabViewItem { DataContext: EditorTabItem tabViewModel })
            {
                viewModel.SelectedTabItem.Value = tabViewModel;
            }
            else
            {
                viewModel.SelectedTabItem.Value = null;
            }
        }
    }

    private void TabItems_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                for (int i = e.NewStartingIndex; i < _tabItems.Count; i++)
                {
                    FATabViewItem? item = _tabItems[i];
                    if (item.DataContext is EditorTabItem itemViewModel)
                    {
                        itemViewModel.Order = i;
                    }
                }
                break;

            case NotifyCollectionChangedAction.Move:
            case NotifyCollectionChangedAction.Replace:
            case NotifyCollectionChangedAction.Reset:
                throw new Exception("Not supported action (Move, Replace, Reset).");
            case NotifyCollectionChangedAction.Remove:
                for (int i = e.OldStartingIndex; i < _tabItems.Count; i++)
                {
                    FATabViewItem? item = _tabItems[i];
                    if (item.DataContext is EditorTabItem itemViewModel)
                    {
                        itemViewModel.Order = i;
                    }
                }
                break;
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

#pragma warning disable RCS1163, IDE0060
    public void AddButtonClick(FATabView? sender, EventArgs e)
#pragma warning restore RCS1163, IDE0060
    {
        if (Resources["AddButtonFlyout"] is MenuFlyout flyout)
        {
            flyout.ShowAt(tabview, true);
        }
    }
}
