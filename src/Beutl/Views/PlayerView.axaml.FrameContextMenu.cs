using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

using Beutl.Graphics;
using Beutl.Helpers;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.ViewModels;
using Beutl.ViewModels.Dialogs;
using Beutl.Views.Dialogs;

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
        if (DataContext is PlayerViewModel viewModel && viewModel.IsCameraMode.Value)
        {
            e.Cancel = true;
            return;
        }

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

    private static async Task SaveImage(IStorageFile file, Bitmap bitmap)
    {
        string str = file.Path.ToString();
        EncodedImageFormat format = Graphics.Image.ToImageFormat(str);

        using Stream stream = await file.OpenWriteAsync();

        bitmap.Save(stream, format);
    }

    // Prompts for the output-resolution multiplier before a save-as-image render. Returns the chosen
    // scale, or null on cancel. The dialog's size-preview / buffer-limit guard is sized against
    // <paramref name="baseSize"/>: the scene frame size for a full frame, the element's measured bounds otherwise.
    private static async Task<float?> PromptSaveScale(PixelSize baseSize)
    {
        var dialogViewModel = new SaveFrameDialogViewModel(baseSize);
        var dialog = new SaveFrameDialog { DataContext = dialogViewModel };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;

        return dialogViewModel.SelectedScale.Value;
    }

    private async void OnSaveElementAsImageClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is { } storage
            && DataContext is PlayerViewModel { Scene: Scene scene } viewModel
            && _lastSelected.TryGetTarget(out Drawable? drawable))
        {
            try
            {
                // Element render bounds can exceed the scene frame, so size the guard against the
                // element's actual bounds, not scene.FrameSize.
                PixelSize elementSize = await viewModel.MeasureSelectedDrawable(drawable);

                // An element that renders nothing measures 0×0, which would size a 0×0 surface and fail
                // mid-render. Report it up-front instead of opening a dialog whose Save is doomed.
                if (!SaveFrameScale.ProducesRenderableSurface(elementSize, 1f))
                {
                    NotificationService.ShowInformation(
                        string.Empty, MessageStrings.SaveImageElementRendersNothing);
                    return;
                }

                if (await PromptSaveScale(elementSize) is not { } scale) return;

                Type type = drawable.GetType();
                string additional = TypeDisplayHelpers.GetLocalizedName(type);
                IStorageFile? file = await SaveImageFilePicker(additional, storage);

                if (file != null)
                {
                    using Bitmap bitmap = await viewModel.DrawSelectedDrawable(drawable, scale);
                    await SaveImage(file, bitmap);
                    _logger.LogInformation("Selected element saved as image: {FilePath}", file.Path);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save selected element as image.");
                NotificationService.ShowError(MessageStrings.FailedToSaveImage, ex.Message);
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
                if (await PromptSaveScale(scene.FrameSize) is not { } scale) return;

                string additional = Path.GetFileNameWithoutExtension(scene.Uri!.LocalPath);
                IStorageFile? file = await SaveImageFilePicker(additional, storage);

                if (file != null)
                {
                    using Bitmap bitmap = await viewModel.DrawFrameAtScale(scale);
                    await SaveImage(file, bitmap);
                    _logger.LogInformation("Frame saved as image: {FilePath}", file.Path);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save frame as image.");
                NotificationService.ShowError(MessageStrings.FailedToSaveImage, ex.Message);
            }
        }
    }
}
