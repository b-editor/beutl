using System.Reactive.Linq;

using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

using BeUtl.Controls;
using BeUtl.Framework;
using BeUtl.Media;
using BeUtl.Media.Pixel;
using BeUtl.ProjectSystem;
using BeUtl.Services;
using BeUtl.ViewModels;
using BeUtl.ViewModels.Editors;

using Microsoft.Extensions.DependencyInjection;

namespace BeUtl.Views;

public sealed partial class EditView : UserControl
{
    private readonly SynchronizationContext _syncContext;
    private Image? _image;
    private FileSystemWatcher? _watcher;
    private IDisposable? _disposable;

    public EditView()
    {
        Resources["LayerEditorConverter"] = new FuncValueConverter<Layer?, object?>(obj =>
        {
            if (obj == null)
                return null;

            return new PropertiesEditorViewModel(obj);
        });
        InitializeComponent();
        _syncContext = SynchronizationContext.Current!;
    }

    private Image Image => _image ??= Player.GetImage();

    protected override void OnAttachedToLogicalTree(Avalonia.LogicalTree.LogicalTreeAttachmentEventArgs e)
    {
        static object? DataContextFactory(string filename)
        {
            ProjectService service = ServiceLocator.Current.GetRequiredService<ProjectService>();
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
        ProjectService service = ServiceLocator.Current.GetRequiredService<ProjectService>();
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
            _disposable?.Dispose();
            _disposable = vm.Scene.GetPropertyChangedObservable(Scene.RendererProperty)
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
        }
    }

    private void Player_PlayButtonClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditViewModel { Player: PlayerViewModel player })
        {
            if (Player.IsPlaying)
            {
                player.Play();
            }
            else
            {
                player.Pause();
            }
        }
    }

    private void Player_NextButtonClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditViewModel { Player: PlayerViewModel player })
        {
            player.Next();
        }
    }

    private void Player_PreviousButtonClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditViewModel { Player: PlayerViewModel player })
        {
            player.Previous();
        }
    }

    private void Player_StartButtonClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditViewModel { Player: PlayerViewModel player })
        {
            player.Start();
        }
    }

    private void Player_EndButtonClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditViewModel { Player: PlayerViewModel player })
        {
            player.End();
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
