using System.Collections.Specialized;

using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

using Beutl.Controls;
using Beutl.Framework;
using Beutl.Framework.Services;
using Beutl.Media;
using Beutl.Media.Pixel;
using Beutl.ProjectSystem;
using Beutl.ViewModels;

using FluentAvalonia.UI.Controls;

using Microsoft.Extensions.DependencyInjection;

namespace Beutl.Views;

public sealed partial class EditView : UserControl, IEditor
{
    private readonly SynchronizationContext _syncContext;
    private static readonly Binding s_isSelectedBinding = new("Context.IsSelected.Value", BindingMode.TwoWay);
    private static readonly Binding s_headerBinding = new("Context.Header");
    private readonly AvaloniaList<TabViewItem> _bottomTabItems = new();
    private readonly AvaloniaList<TabViewItem> _rightTabItems = new();
    private Image? _image;
    //private FileSystemWatcher? _watcher;
    private IDisposable? _disposable0;
    private IDisposable? _disposable1;
    private IDisposable? _disposable2;

    public EditView()
    {
        InitializeComponent();
        _syncContext = SynchronizationContext.Current!;

        // 下部のタブ
        BottomTabView.TabItems = _bottomTabItems;
        _bottomTabItems.CollectionChanged += TabItems_CollectionChanged;

        // 右側のタブ
        RightTabView.TabItems = _rightTabItems;
        _rightTabItems.CollectionChanged += TabItems_CollectionChanged;
    }

    private Image Image => _image ??= Player.GetImage();

    private void TabItems_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (sender is TabView { TabItems: AvaloniaList<TabViewItem> tabItems })
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    for (int i = e.NewStartingIndex; i < tabItems.Count; i++)
                    {
                        TabViewItem? item = tabItems[i];
                        if (item.DataContext is ToolTabViewModel itemViewModel)
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
                    for (int i = e.OldStartingIndex; i < tabItems.Count; i++)
                    {
                        TabViewItem? item = tabItems[i];
                        if (item.DataContext is ToolTabViewModel itemViewModel)
                        {
                            itemViewModel.Order = i;
                        }
                    }
                    break;
                default:
                    break;
            }
        }
    }

    protected override void OnAttachedToLogicalTree(Avalonia.LogicalTree.LogicalTreeAttachmentEventArgs e)
    {
        //static object? DataContextFactory(string filename)
        //{
        //    IProjectService service = ServiceLocator.Current.GetRequiredService<IProjectService>();
        //    if (service.CurrentProject.Value != null)
        //    {
        //        foreach (IStorable item in service.CurrentProject.Value.EnumerateAllChildren<IStorable>())
        //        {
        //            if (item.FileName == filename)
        //            {
        //                return item;
        //            }
        //        }
        //    }

        //    return null;
        //}

        base.OnAttachedToLogicalTree(e);
        IProjectService service = ServiceLocator.Current.GetRequiredService<IProjectService>();
        if (service.CurrentProject.Value != null)
        {
            //_watcher = new FileSystemWatcher(service.CurrentProject.Value.RootDirectory)
            //{
            //    EnableRaisingEvents = true,
            //    IncludeSubdirectories = true,
            //};
            //Explorer.Content = new DirectoryTreeView(_watcher, DataContextFactory);
        }
    }

    protected override void OnDetachedFromLogicalTree(Avalonia.LogicalTree.LogicalTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromLogicalTree(e);
        //Explorer.Content = null;
        //_watcher?.Dispose();
        //_watcher = null;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is EditViewModel vm)
        {
            vm.Scene.Renderer.RenderInvalidated += Renderer_RenderInvalidated;
            _disposable0?.Dispose();
            _disposable0 = vm.Scene.GetPropertyChangedObservable(Scene.RendererProperty)
                .Subscribe(a =>
                {
                    if (a.OldValue != null)
                    {
                        a.OldValue.RenderInvalidated -= Renderer_RenderInvalidated;
                    }

                    if (a.NewValue != null)
                    {
                        a.NewValue.RenderInvalidated += Renderer_RenderInvalidated;
                    }
                });

            _disposable1?.Dispose();
            _disposable1 = vm.BottomTabItems.ForEachItem(
                (item) =>
                {
                    ToolTabExtension ext = item.Context.Extension;
                    if (DataContext is not IEditorContext editorContext || !item.Context.Extension.TryCreateContent(editorContext, out IControl? control))
                    {
                        control = new TextBlock()
                        {
                            Text = @$"
Error:
    {Message.CannotDisplayThisContext}"
                        };
                    }

                    control.DataContext = item.Context;
                    var tabItem = new TabViewItem
                    {
                        [!TabViewItem.HeaderProperty] = s_headerBinding,
                        [!ListBoxItem.IsSelectedProperty] = s_isSelectedBinding,
                        DataContext = item,
                        Content = control,
                    };

                    tabItem.CloseRequested += (s, _) =>
                    {
                        if (s is TabViewItem { DataContext: ToolTabViewModel tabViewModel } && DataContext is IEditorContext viewModel)
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
                        TabViewItem tabItem = _bottomTabItems[i];
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
                    if (DataContext is not IEditorContext editorContext || !item.Context.Extension.TryCreateContent(editorContext, out IControl? control))
                    {
                        control = new TextBlock()
                        {
                            Text = @$"
Error:
    {Message.CannotDisplayThisContext}"
                        };
                    }

                    control.DataContext = item.Context;
                    var tabItem = new TabViewItem
                    {
                        [!TabViewItem.HeaderProperty] = s_headerBinding,
                        [!ListBoxItem.IsSelectedProperty] = s_isSelectedBinding,
                        DataContext = item,
                        Content = control,
                    };

                    tabItem.CloseRequested += (s, _) =>
                    {
                        if (s is TabViewItem { DataContext: ToolTabViewModel tabViewModel } && DataContext is IEditorContext viewModel)
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
                        TabViewItem tabItem = _rightTabItems[i];
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
        }
    }

    private unsafe void Renderer_RenderInvalidated(object? sender, Rendering.IRenderer.RenderResult e)
    {
        if (Image == null)
            return;

        _syncContext.Send(_ =>
        {
            Bitmap<Bgra8888> img = e.Bitmap;
            WriteableBitmap bitmap;

            if (Image.Source is WriteableBitmap bitmap1 &&
                bitmap1.PixelSize.Width == img.Width &&
                bitmap1.PixelSize.Height == img.Height)
            {
                bitmap = bitmap1;
            }
            else
            {
                bitmap = new WriteableBitmap(
                    new(img.Width, img.Height),
                    new(96, 96),
                    PixelFormat.Bgra8888, AlphaFormat.Premul);
            }

            Image.Source = bitmap;
            using (ILockedFramebuffer buf = bitmap.Lock())
            {
                int size = img.ByteCount;
                Buffer.MemoryCopy((void*)img.Data, (void*)buf.Address, size, size);
            }

            Image.InvalidateVisual();
        }, null);
    }
}
