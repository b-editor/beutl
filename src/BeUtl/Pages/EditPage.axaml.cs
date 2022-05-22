using System.Collections.Specialized;
using System.Reactive.Linq;

using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;

using BeUtl.Collections;
using BeUtl.Framework;
using BeUtl.Services;
using BeUtl.ViewModels;
using BeUtl.Views;
using BeUtl.Views.Dialogs;

using Reactive.Bindings;

using FAPathIconSource = FluentAvalonia.UI.Controls.PathIconSource;
using FATabView = FluentAvalonia.UI.Controls.TabView;
using FATabViewItem = FluentAvalonia.UI.Controls.TabViewItem;
using S = BeUtl.Language.StringResources;

namespace BeUtl.Pages;

public sealed partial class EditPage : UserControl
{
    private static readonly Binding s_headerBinding = new("FileName.Value");
    private static readonly Binding s_iconSourceBinding = new("Extension.Value.Icon")
    {
        Converter = new FuncValueConverter<Geometry?, FAPathIconSource?>(
            geometry => geometry != null
                            ? new FAPathIconSource { Data = geometry }
                            : null)
    };
    private static readonly Binding s_isSelectedBinding = new("IsSelected.Value", BindingMode.TwoWay);
    private static readonly Binding s_contentBinding = new("Value", BindingMode.OneWay);
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
                    var tabItem = new FATabViewItem
                    {
                        [!FATabViewItem.HeaderProperty] = s_headerBinding,
                        [!FATabViewItem.IconSourceProperty] = s_iconSourceBinding,
                        [!ListBoxItem.IsSelectedProperty] = s_isSelectedBinding,
                        DataContext = item,
                        Content = new ContentControl
                        {
                            [!ContentProperty] = s_contentBinding,
                            DataContext = item.Context.Select<IEditorContext, IControl>(obj =>
                            {
                                if (obj?.Extension.TryCreateEditor(obj.EdittingFile, out IEditor? editor) == true)
                                {
                                    editor.DataContext = obj;
                                    return editor;
                                }
                                else
                                {
                                    return new TextBlock()
                                    {
                                        Text = obj != null ? @$"
Error:
    {string.Format(S.Message.CouldNotOpenFollowingFileWithExtension, obj.Extension.DisplayName, Path.GetFileName(obj.EdittingFile))}

Message:
    {S.Message.EditorContextHasAlreadyBeenCreated}
                " : @$"
Error:
    {S.Message.NullWasSpecifiedForEditorContext}
                "
                                    };
                                }
                            }).ToReadOnlyReactivePropertySlim(),
                        }
                    };

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
