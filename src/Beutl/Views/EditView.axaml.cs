using System.Collections.Specialized;

using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Threading;

using Beutl.Controls;
using Beutl.Services;
using Beutl.ViewModels;

namespace Beutl.Views;

public sealed partial class EditView : UserControl
{
    private static readonly Binding s_isSelectedBinding = new("Context.IsSelected.Value", BindingMode.TwoWay);
    private static readonly Binding s_headerBinding = new("Context.Header");
    private readonly AvaloniaList<BcTabItem> _bottomTabItems = new();
    private readonly AvaloniaList<BcTabItem> _rightTabItems = new();
    private Image? _image;
    private IDisposable? _disposable1;
    private IDisposable? _disposable2;
    private IDisposable? _disposable3;

    public EditView()
    {
        InitializeComponent();

        // 下部のタブ
        BottomTabView.ItemsSource = _bottomTabItems;
        BottomTabView.GetObservable(SelectingItemsControl.SelectedItemProperty).Subscribe(OnTabViewSelectedItemChanged);
        _bottomTabItems.CollectionChanged += TabItems_CollectionChanged;

        // 右側のタブ
        RightTabView.ItemsSource = _rightTabItems;
        RightTabView.GetObservable(SelectingItemsControl.SelectedItemProperty).Subscribe(OnTabViewSelectedItemChanged);
        _rightTabItems.CollectionChanged += TabItems_CollectionChanged;

        this.GetObservable(IsKeyboardFocusWithinProperty)
            .Subscribe(v => Player.SetSeekBarOpacity(v ? 1 : 0.8));
    }

    private void OnTabViewSelectedItemChanged(object? obj)
    {
        if (obj is BcTabItem { DataContext: ToolTabViewModel itemViewModel })
        {
            Telemetry.ToolTabSelected(itemViewModel.Context.Extension.Name);
        }
    }

    private Image Image => _image ??= Player.GetImage();

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (DataContext is EditViewModel viewModel)
        {
            // TextBox.OnKeyDown で e.Handled が True に設定されないので
            if (e.Key == Key.Space && e.Source is TextBox)
            {
                return;
            }

            // KeyBindingsは変更してはならない。
            foreach (KeyBinding binding in viewModel.KeyBindings)
            {
                if (e.Handled)
                    break;
                binding.TryHandle(e);
            }
        }
    }

    private void TabItems_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        static void OnAdded(NotifyCollectionChangedEventArgs e, AvaloniaList<BcTabItem> tabItems)
        {
            for (int i = e.NewStartingIndex; i < tabItems.Count; i++)
            {
                BcTabItem? item = tabItems[i];
                if (item.DataContext is ToolTabViewModel itemViewModel)
                {
                    itemViewModel.Order = i;
                }
            }
        }

        static void OnRemoved(NotifyCollectionChangedEventArgs e, AvaloniaList<BcTabItem> tabItems)
        {
            for (int i = e.OldStartingIndex; i < tabItems.Count; i++)
            {
                BcTabItem? item = tabItems[i];
                if (item.DataContext is ToolTabViewModel itemViewModel)
                {
                    itemViewModel.Order = i;
                }
            }
        }

        if (sender is BcTabView { ItemsSource: AvaloniaList<BcTabItem> tabItems })
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    OnAdded(e, tabItems);
                    break;

                case NotifyCollectionChangedAction.Move:
                    OnRemoved(e, tabItems);
                    OnAdded(e, tabItems);
                    break;

                case NotifyCollectionChangedAction.Replace:
                case NotifyCollectionChangedAction.Reset:
                    throw new Exception("Not supported action (Move, Replace, Reset).");

                case NotifyCollectionChangedAction.Remove:
                    OnRemoved(e, tabItems);
                    break;
            }
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (DataContext is EditViewModel viewModel && viewModel.Player.IsPlaying.Value)
        {
            viewModel.Player.Pause();
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is EditViewModel vm)
        {
            _disposable1?.Dispose();
            _disposable1 = vm.BottomTabItems.ForEachItem(
                (item) =>
                {
                    ToolTabExtension ext = item.Context.Extension;
                    if (DataContext is not IEditorContext editorContext || !item.Context.Extension.TryCreateContent(editorContext, out Control? control))
                    {
                        control = new TextBlock()
                        {
                            Text = @$"
Error:
    {Message.CannotDisplayThisContext}"
                        };
                    }

                    control.DataContext = item.Context;
                    var tabItem = new BcTabItem
                    {
                        [!HeaderedContentControl.HeaderProperty] = s_headerBinding,
                        [!TabItem.IsSelectedProperty] = s_isSelectedBinding,
                        DataContext = item,
                        Content = control,
                    };

                    tabItem.CloseButtonClick += (s, _) =>
                    {
                        if (s is BcTabItem { DataContext: ToolTabViewModel tabViewModel } && DataContext is IEditorContext viewModel)
                        {
                            viewModel.CloseToolTab(tabViewModel.Context);
                        }
                    };

                    if (item.Order < 0 || item.Order > _bottomTabItems.Count)
                    {
                        item.Order = _bottomTabItems.Count;
                    }

                    _bottomTabItems.Insert(item.Order, tabItem);
                },
                (item) =>
                {
                    for (int i = 0; i < _bottomTabItems.Count; i++)
                    {
                        BcTabItem tabItem = _bottomTabItems[i];
                        if (tabItem.DataContext is ToolTabViewModel itemViewModel
                            && itemViewModel.Context == item.Context)
                        {
                            itemViewModel.Order = -1;
                            _bottomTabItems.RemoveAt(i);
                            return;
                        }
                    }
                },
                () => throw new Exception());

            _disposable2?.Dispose();
            _disposable2 = vm.RightTabItems.ForEachItem(
                (item) =>
                {
                    ToolTabExtension ext = item.Context.Extension;
                    if (DataContext is not IEditorContext editorContext || !item.Context.Extension.TryCreateContent(editorContext, out Control? control))
                    {
                        control = new TextBlock()
                        {
                            Text = @$"
Error:
    {Message.CannotDisplayThisContext}"
                        };
                    }

                    control.DataContext = item.Context;
                    var tabItem = new BcTabItem
                    {
                        [!HeaderedContentControl.HeaderProperty] = s_headerBinding,
                        [!TabItem.IsSelectedProperty] = s_isSelectedBinding,
                        DataContext = item,
                        Content = control,
                    };

                    tabItem.CloseButtonClick += (s, _) =>
                    {
                        if (s is BcTabItem { DataContext: ToolTabViewModel tabViewModel } && DataContext is IEditorContext viewModel)
                        {
                            viewModel.CloseToolTab(tabViewModel.Context);
                        }
                    };

                    if (item.Order < 0 || item.Order > _rightTabItems.Count)
                    {
                        item.Order = _rightTabItems.Count;
                    }

                    _rightTabItems.Insert(item.Order, tabItem);
                },
                (item) =>
                {
                    for (int i = 0; i < _rightTabItems.Count; i++)
                    {
                        BcTabItem tabItem = _rightTabItems[i];
                        if (tabItem.DataContext is ToolTabViewModel itemViewModel
                            && itemViewModel.Context == item.Context)
                        {
                            itemViewModel.Order = -1;
                            _rightTabItems.RemoveAt(i);
                            return;
                        }
                    }
                },
                () => throw new Exception());

            _disposable3?.Dispose();
            vm.Player.PreviewInvalidated += Player_PreviewInvalidated;
            _disposable3 = Disposable.Create(vm, x => x.Player.PreviewInvalidated -= Player_PreviewInvalidated);
        }
    }

    private void Player_PreviewInvalidated(object? sender, EventArgs e)
    {
        if (Image == null)
            return;

        Dispatcher.UIThread.InvokeAsync(Image.InvalidateVisual);
    }
}
