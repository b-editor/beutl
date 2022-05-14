using System.Collections;
using System.Reactive.Linq;

using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Interactivity;
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
using BeUtl.Services;
using BeUtl.ViewModels;
using BeUtl.ViewModels.Editors;

using Microsoft.Extensions.DependencyInjection;

using FAPathIconSource = FluentAvalonia.UI.Controls.PathIconSource;
using FATabView = FluentAvalonia.UI.Controls.TabView;
using FATabViewItem = FluentAvalonia.UI.Controls.TabViewItem;

namespace BeUtl.Views;

public sealed partial class EditView : UserControl, IEditor
{
    private readonly SynchronizationContext _syncContext;
    private readonly Binding _bottomHeightBinding;
    private readonly Binding _rightHeightBinding;
    private Image? _image;
    private FileSystemWatcher? _watcher;
    private IDisposable? _disposable0;
    private IDisposable? _disposable1;
    private IDisposable? _disposable2;

    public EditView()
    {
        InitializeComponent();
        _syncContext = SynchronizationContext.Current!;
        _bottomHeightBinding = new Binding("Bounds.Height")
        {
            Source = timeline
        };
        _rightHeightBinding = new Binding("Bounds.Height")
        {
            Source = propertiesEditor
        };
    }

    private Image Image => _image ??= Player.GetImage();

    protected override void OnAttachedToLogicalTree(Avalonia.LogicalTree.LogicalTreeAttachmentEventArgs e)
    {
        static object? DataContextFactory(string filename)
        {
            IProjectService service = ServiceLocator.Current.GetRequiredService<IProjectService>();
            if (service.CurrentProject.Value != null)
            {
                foreach (IStorable item in service.CurrentProject.Value.EnumerateAllChildren<IStorable>())
                {
                    if (item.FileName == filename)
                    {
                        return item;
                    }
                }
            }

            return null;
        }

        base.OnAttachedToLogicalTree(e);
        IProjectService service = ServiceLocator.Current.GetRequiredService<IProjectService>();
        if (service.CurrentProject.Value != null)
        {
            _watcher = new FileSystemWatcher(service.CurrentProject.Value.RootDirectory)
            {
                EnableRaisingEvents = true,
                IncludeSubdirectories = true,
            };
            Explorer.Content = new DirectoryTreeView(_watcher, DataContextFactory);
        }
    }

    protected override void OnDetachedFromLogicalTree(Avalonia.LogicalTree.LogicalTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromLogicalTree(e);
        Explorer.Content = null;
        _watcher?.Dispose();
        _watcher = null;
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
            _disposable1 = vm.AnimationTimelines.ForEachItem(
                (item) =>
                {
                    if (BottomTabView.TabItems is not IList list) return;

                    var tabItem = new FATabViewItem
                    {
                        // Todo: Bindingをキャッシュする
                        [!ListBoxItem.IsSelectedProperty] = new Binding("IsSelected.Value", BindingMode.TwoWay),
                        Header = $"{item.Layer.Name} / {item.Setter.Property.Name}",
                        DataContext = item,
                        Content = new AnimationTimeline(),
                        IsClosable = true
                    };
                    list.Add(tabItem);
                    tabItem.CloseRequested += (s, _) =>
                    {
                        if (DataContext is EditViewModel viewModel
                            && s.DataContext is AnimationTimelineViewModel anmViewModel)
                        {
                            viewModel.AnimationTimelines.Remove(anmViewModel);
                        }
                    };
                },
                (item) =>
                {
                    if (BottomTabView.TabItems is not IList list) return;

                    FATabViewItem? tabItem = BottomTabView.TabItems
                        .OfType<FATabViewItem>()
                        .FirstOrDefault(i => i.DataContext == item);

                    if (tabItem != null)
                    {
                        list.Remove(tabItem);
                        item.Dispose();
                    }
                },
                () =>
                {
                    if (BottomTabView.TabItems is not IList list) return;

                    foreach (FATabViewItem item in BottomTabView.TabItems
                        .OfType<FATabViewItem>()
                        .Where(v => v.DataContext is AnimationTimelineViewModel).ToArray())
                    {
                        list.Remove(item);
                        (item.DataContext as IDisposable)?.Dispose();
                    }
                });

            _disposable2?.Dispose();
            _disposable2 = vm.UsingExtensions.ForEachItem(
                (item) =>
                {
                    SceneEditorTabExtension extension = item.Extension;
                    FATabView tabView = extension.Placement == SceneEditorTabExtension.TabPlacement.Bottom ? BottomTabView : RightTabView;
                    if (tabView.TabItems is IList list && DataContext is EditViewModel viewModel)
                    {
                        var tabItem = new FATabViewItem()
                        {
                            // Todo: Bindingをキャッシュする
                            [!ListBoxItem.IsSelectedProperty] = new Binding("IsSelected.Value", BindingMode.TwoWay),
                            [!FATabViewItem.HeaderProperty] = new DynamicResourceExtension(extension.Header.Key),
                            Content = extension.CreateContent(viewModel.Scene),
                            DataContext = item,
                            IsClosable = extension.IsClosable
                        };

                        if (tabItem.Content is Layoutable content)
                        {
                            content[!HeightProperty] =
                                extension.Placement == SceneEditorTabExtension.TabPlacement.Bottom
                                ? _bottomHeightBinding
                                : _rightHeightBinding;
                        }

                        if (extension.Icon != null)
                        {
                            tabItem.IconSource = new FAPathIconSource
                            {
                                Data = extension.Icon
                            };
                        }

                        list.Add(tabItem);

                        tabItem.CloseRequested += (s, _) =>
                        {
                            if (DataContext is EditViewModel viewModel
                                && s.DataContext is ExtendedEditTabViewModel tabViewModel)
                            {
                                viewModel.UsingExtensions.Remove(tabViewModel);
                            }
                        };
                    }
                },
                (item) =>
                {
                    SceneEditorTabExtension extension = item.Extension;
                    FATabView tabView = extension.Placement == SceneEditorTabExtension.TabPlacement.Bottom ? BottomTabView : RightTabView;
                    if (tabView.TabItems is IList list && DataContext is EditViewModel viewModel)
                    {
                        FATabViewItem? tabItem = list
                            .OfType<FATabViewItem>()
                            .FirstOrDefault(i => i.DataContext == item);

                        if (tabItem != null)
                        {
                            list.Remove(tabItem);
                            item.Dispose();
                        }
                    }
                },
                () =>
                {
                    if (BottomTabView.TabItems is IList list0
                        && RightTabView.TabItems is IList list1)
                    {
                        foreach (FATabViewItem item in list0
                            .OfType<FATabViewItem>()
                            .Where(v => v.DataContext is ExtendedEditTabViewModel).ToArray())
                        {
                            list0.Remove(item);
                            (item.DataContext as IDisposable)?.Dispose();
                        }

                        foreach (FATabViewItem item in list1
                            .OfType<FATabViewItem>()
                            .Where(v => v.DataContext is ExtendedEditTabViewModel).ToArray())
                        {
                            list1.Remove(item);
                            (item.DataContext as IDisposable)?.Dispose();
                        }
                    }
                });
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
