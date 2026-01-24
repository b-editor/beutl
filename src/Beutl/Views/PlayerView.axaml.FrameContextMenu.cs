using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.Pixel;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.ViewModels;

using FluentAvalonia.UI.Controls;

using Microsoft.Extensions.Logging;

namespace Beutl.Views;

public partial class PlayerView
{
    private MenuItem? _saveElementAsImage;
    private MenuItem? _saveFrameAsImage;
    private void ConfigureFrameContextMenu(Control control)
    {
        _saveElementAsImage = new MenuItem
        {
            Header = Strings.SaveSelectedElementAsImage,
            IsEnabled = false
        };
        _saveElementAsImage.Click += OnSaveElementAsImageClick;
        _saveFrameAsImage = new MenuItem
        {
            Header = Strings.SaveFrameAsImage,
            Icon = new SymbolIcon
            {
                Symbol = Symbol.Image
            },
        };
        _saveFrameAsImage.Click += OnSaveFrameAsImageClick;
        var resetZoom = new MenuItem
        {
            Header = Strings.ResetZoom
        };
        resetZoom.Click += OnResetZoomClick;

        var menu = new ContextMenu()
        {
            Items =
            {
                _saveFrameAsImage,
                _saveElementAsImage,
                resetZoom
            }
        };
        menu.Opening += FrameContextMenuOpening;
        control.ContextMenu = menu;
    }

    private void OnResetZoomClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PlayerViewModel viewModel)
        {
            viewModel.FrameMatrix.Value = Matrix.Identity;
            _logger.LogInformation("Zoom reset to default.");
        }
    }

    private void FrameContextMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_saveElementAsImage != null)
        {
            _saveElementAsImage.IsEnabled = _lastSelected.TryGetTarget(out _);
        }
    }

    private static async Task<IStorageFile?> SaveImageFilePicker(string name, IStorageProvider storage)
    {
        FilePickerSaveOptions options = SharedFilePickerOptions.SaveImage();
        options.SuggestedFileName = $"{name} {DateTime.Now:yyyy-dd-MM HHmmss}";
        options.SuggestedStartLocation = await storage.TryGetWellKnownFolderAsync(WellKnownFolder.Pictures);
        options.DefaultExtension = "png";
        return await storage.SaveFilePickerAsync(options);
    }

    private static async Task SaveImage(IStorageFile file, Bitmap<Bgra8888> bitmap)
    {
        string str = file.Path.ToString();
        EncodedImageFormat format = Graphics.Image.ToImageFormat(str);

        using Stream stream = await file.OpenWriteAsync();

        bitmap.Save(stream, format);
    }

    private async void OnSaveElementAsImageClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is { } storage
            && DataContext is PlayerViewModel { Scene: Scene scene } viewModel
            && _lastSelected.TryGetTarget(out Drawable? drawable))
        {
            try
            {
                Task<Bitmap<Bgra8888>> renderTask = viewModel.DrawSelectedDrawable(drawable);

                FilePickerSaveOptions options = SharedFilePickerOptions.SaveImage();
                Type type = drawable.GetType();
                string additional = TypeDisplayHelpers.GetLocalizedName(type);
                IStorageFile? file = await SaveImageFilePicker(additional, storage);

                if (file != null)
                {
                    using Bitmap<Bgra8888> bitmap = await renderTask;
                    await SaveImage(file, bitmap);
                    _logger.LogInformation("Selected element saved as image: {FilePath}", file.Path);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save selected element as image.");
                NotificationService.ShowError(Message.Failed_to_save_image, ex.Message);
            }
        }
    }

    private async void OnSaveFrameAsImageClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is { } storage
            && DataContext is PlayerViewModel { Scene: Scene scene } viewModel)
        {
            try
            {
                Task<Bitmap<Bgra8888>> renderTask = viewModel.DrawFrame();

                string additional = Path.GetFileNameWithoutExtension(scene.Uri!.LocalPath);
                IStorageFile? file = await SaveImageFilePicker(additional, storage);

                if (file != null)
                {
                    using Bitmap<Bgra8888> bitmap = await renderTask;
                    await SaveImage(file, bitmap);
                    _logger.LogInformation("Frame saved as image: {FilePath}", file.Path);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save frame as image.");
                NotificationService.ShowError(Message.Failed_to_save_image, ex.Message);
            }
        }
    }
}
