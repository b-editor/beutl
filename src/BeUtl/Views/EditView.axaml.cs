using System.Collections;
using System.Collections.Specialized;
using System.Reactive.Linq;

using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

using BeUtl.Collections;
using BeUtl.Controls;
using BeUtl.Framework;
using BeUtl.Framework.Services;
using BeUtl.Media;
using BeUtl.Media.Pixel;
using BeUtl.ProjectSystem;
using BeUtl.ViewModels;

using Microsoft.Extensions.DependencyInjection;

using FAPathIconSource = FluentAvalonia.UI.Controls.PathIconSource;
using FATabView = FluentAvalonia.UI.Controls.TabView;
using FATabViewItem = FluentAvalonia.UI.Controls.TabViewItem;

namespace BeUtl.Views;

public sealed partial class EditView : UserControl, IEditor
{
    private readonly SynchronizationContext _syncContext;
    private static readonly Binding s_isSelectedBinding = new("IsSelected.Value", BindingMode.TwoWay);
    private static readonly Binding s_headerBinding = new("Header.Value");
    //private readonly Binding _bottomHeightBinding;
    //private readonly Binding _rightHeightBinding;
    private readonly AvaloniaList<FATabViewItem> _bottomTabItems = new();
    private readonly AvaloniaList<FATabViewItem> _rightTabItems = new();
    private Image? _image;
    //private FileSystemWatcher? _watcher;
    private IDisposable? _disposable0;
    private IDisposable? _disposable1;
    private IDisposable? _disposable2;

    public EditView()
    {
        InitializeComponent();
        _syncContext = SynchronizationContext.Current!;

        BottomTabView.TabItems = _bottomTabItems;
        RightTabView.TabItems = _rightTabItems;
        //_bottomHeightBinding = new Binding("Bounds.Height")
        //{
        //    Source = timeline
        //};
        //_rightHeightBinding = new Binding("Bounds.Height")
        //{
        //    Source = propertiesEditor
        //};
    }

    private Image Image => _image ??= Player.GetImage();

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
                (index, item) =>
                {
                    ToolTabExtension ext = item.Extension;
                    if (DataContext is not IEditorContext editorContext || !item.Extension.TryCreateContent(editorContext, out IControl? control))
                    {
                        control = new TextBlock()
                        {
                            Text = @$"
Error:
    {StringResources.Message.CannotDisplayThisContext}"
                        };
                    }

                    var tabItem = new FATabViewItem
                    {
                        [!FATabViewItem.HeaderProperty] = s_headerBinding,
                        [!ListBoxItem.IsSelectedProperty] = s_isSelectedBinding,
                        DataContext = item,
                        Content = control,
                    };

                    tabItem.CloseRequested += (s, _) =>
                    {
                        if (s is FATabViewItem { DataContext: IToolContext toolContext } && DataContext is IEditorContext viewModel)
                        {
                            viewModel.CloseToolTab(toolContext);
                        }
                    };

                    _bottomTabItems.Insert(index, tabItem);
                },
                (index, _) => _bottomTabItems.RemoveAt(index),
                () => throw new Exception());

            _disposable2?.Dispose();
            _disposable2 = vm.RightTabItems.ForEachItem(
                (index, item) =>
                {
                    ToolTabExtension ext = item.Extension;
                    if (DataContext is not IEditorContext editorContext || !item.Extension.TryCreateContent(editorContext, out IControl? control))
                    {
                        control = new TextBlock()
                        {
                            Text = @$"
Error:
    {StringResources.Message.CannotDisplayThisContext}"
                        };
                    }

                    var tabItem = new FATabViewItem
                    {
                        [!FATabViewItem.HeaderProperty] = s_headerBinding,
                        [!ListBoxItem.IsSelectedProperty] = s_isSelectedBinding,
                        DataContext = item,
                        Content = control,
                    };

                    tabItem.CloseRequested += (s, _) =>
                    {
                        if (s is FATabViewItem { DataContext: IToolContext toolContext } && DataContext is IEditorContext viewModel)
                        {
                            viewModel.CloseToolTab(toolContext);
                        }
                    };

                    _rightTabItems.Insert(index, tabItem);
                },
                (index, _) => _rightTabItems.RemoveAt(index),
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
