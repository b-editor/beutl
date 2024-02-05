using System.Collections.Specialized;

using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using Avalonia.Threading;

using Beutl.Controls;
using Beutl.Logging;
using Beutl.Services;
using Beutl.ViewModels;

using Microsoft.Extensions.Logging;

using Reactive.Bindings.Extensions;

namespace Beutl.Views;

public sealed partial class EditView : UserControl
{
    private static readonly Binding s_isSelectedBinding = new("Context.IsSelected.Value", BindingMode.TwoWay);
    private static readonly Binding s_headerBinding = new("Context.Header");
    private readonly ILogger _logger = Log.CreateLogger<EditView>();
    private readonly AvaloniaList<BcTabItem> _bottomTabItems = [];
    private readonly AvaloniaList<BcTabItem> _rightTabItems = [];
    private readonly CompositeDisposable _disposables = [];
    private Image? _image;

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

        Player.TemplateApplied += OnPlayerTemplateApplied;
    }

    private Image Image => _image ??= Player.GetImage();

    private void OnPlayerTemplateApplied(object? sender, TemplateAppliedEventArgs e)
    {
        // EditView.axaxml.MouseControl.cs
        Panel control = Player.GetFramePanel();
        ConfigureFrameContextMenu(control);
        control.PointerPressed += OnFramePointerPressed;
        control.PointerReleased += OnFramePointerReleased;
        control.PointerMoved += OnFramePointerMoved;
        control.AddHandler(PointerWheelChangedEvent, OnFramePointerWheelChanged, RoutingStrategies.Tunnel);

        control.GetObservable(BoundsProperty)
            .Subscribe(s =>
            {
                if (DataContext is EditViewModel { Player: { } player })
                {
                    player.MaxFrameSize = new((float)s.Size.Width, (float)s.Size.Height);
                }
            });

        // EditView.axaxml.DragAndDrop.cs
        DragDrop.SetAllowDrop(control, true);
        control.AddHandler(DragDrop.DragOverEvent, OnFrameDragOver);
        control.AddHandler(DragDrop.DropEvent, OnFrameDrop);
    }

    private void OnTabViewSelectedItemChanged(object? obj)
    {
        if (obj is BcTabItem { DataContext: ToolTabViewModel { Context.Extension.Name: string name } })
        {
            _logger.LogInformation("'{ToolTabName}' has been selected.", name);
        }
    }

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
        _disposables.Clear();
        if (DataContext is EditViewModel vm)
        {
            vm.BottomTabItems.ForEachItem(
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
                () => throw new Exception())
                .DisposeWith(_disposables);

            vm.RightTabItems.ForEachItem(
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
                () => throw new Exception())
                .DisposeWith(_disposables);

            vm.Player.PreviewInvalidated += Player_PreviewInvalidated;
            Disposable.Create(vm, x => x.Player.PreviewInvalidated -= Player_PreviewInvalidated)
                .DisposeWith(_disposables);

            vm.Player.FrameMatrix
                .ObserveOnUIDispatcher()
                .Select(matrix => (matrix, Player.GetImage(), Player.GetFramePanel()?.Children?.FirstOrDefault()!))
                .Where(t => t.Item2 != null && t.Item3 != null)
                .Subscribe(t =>
                {
                    t.Item3.RenderTransformOrigin = t.Item2.RenderTransformOrigin = RelativePoint.TopLeft;
                    t.Item3.RenderTransform = t.Item2.RenderTransform = new ImmutableTransform(t.matrix.ToAvaMatrix());
                    if (DataContext is EditViewModel vm)
                    {
                        int width = vm.Scene.FrameSize.Width;
                        if (width == 0) return;
                        double actualWidth = t.Item2.Bounds.Width * t.matrix.M11;
                        double pixelSize = actualWidth / width;
                        if (pixelSize >= 1)
                        {
                            RenderOptions.SetBitmapInterpolationMode(t.Item2, BitmapInterpolationMode.None);
                        }
                        else
                        {
                            RenderOptions.SetBitmapInterpolationMode(t.Item2, BitmapInterpolationMode.HighQuality);
                        }
                    }
                })
                .DisposeWith(_disposables);

            vm.Player.IsHandMode.CombineLatest(vm.Player.IsCropMode)
                .ObserveOnUIDispatcher()
                .Where(_ => Player.GetFramePanel() != null)
                .Subscribe(t =>
                {
                    if (t.First)
                        Player.GetFramePanel().Cursor = Cursors.Hand;
                    else if (t.Second)
                        Player.GetFramePanel().Cursor = Cursors.Cross;
                    else
                        Player.GetFramePanel().Cursor = null;
                })
                .DisposeWith(_disposables);
        }
    }

    private void Player_PreviewInvalidated(object? sender, EventArgs e)
    {
        if (Image == null)
            return;

        Dispatcher.UIThread.InvokeAsync(Image.InvalidateVisual);
    }
}
