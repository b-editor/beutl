using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

using BEditorNext.Framework;
using BEditorNext.Media;
using BEditorNext.Media.Pixel;
using BEditorNext.ProjectSystem;
using BEditorNext.ViewModels;
using BEditorNext.ViewModels.Editors;

namespace BEditorNext.Views;

public sealed partial class EditView : UserControl, IStorableControl
{
    private readonly SynchronizationContext _syncContext;
    private Image? _image;

    public EditView()
    {
        Resources["LayerEditorConverter"] = new FuncValueConverter<SceneLayer?, object?>(obj =>
        {
            if (obj == null)
                return null;

            return new PropertiesEditorViewModel(obj);
        });
        InitializeComponent();
        _syncContext = SynchronizationContext.Current!;
    }

    public string FileName { get; private set; } = Path.GetTempFileName();

    public DateTime LastSavedTime { get; }

    private Image Image => _image ??= Player.GetImage();

    public void Restore(string filename)
    {
    }

    public void Save(string filename)
    {
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is EditViewModel vm)
        {
            vm.Scene.Renderer.RenderRequested += Renderer_RenderRequested;
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

    private void Renderer_RenderRequested(object? sender, Rendering.IRenderer.RenderResult e)
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
            ILockedFramebuffer buf = bitmap.Lock();

            unsafe
            {
                int size = img.ByteCount;
                Buffer.MemoryCopy((void*)img.Data, (void*)buf.Address, size, size);
            }

            buf.Dispose();
            Image.InvalidateVisual();
        }, null);
    }
}
