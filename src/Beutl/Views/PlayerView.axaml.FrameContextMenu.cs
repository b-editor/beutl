using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

using Beutl.Graphics;
using Beutl.Helpers;
using Beutl.IO;
using Beutl.Media;
using Beutl.Models;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.ViewModels;
using Beutl.ViewModels.Dialogs;
using Beutl.Views.Dialogs;

using FluentAvalonia.UI.Controls;

using Microsoft.Extensions.Logging;

using FluentIconSource = FluentIcons.Avalonia.Fluent.FluentIconSource;
using Icon = FluentIcons.Common.Icon;

namespace Beutl.Views;

public partial class PlayerView
{
    private FAMenuFlyoutItem _saveElementAsImage = null!;
    private void ConfigureFrameContextMenu(Control control)
    {
        _saveElementAsImage = new FAMenuFlyoutItem
        {
            Text = Strings.SaveSelectedElementAsImage,
            IsEnabled = false
        };
        _saveElementAsImage.Click += OnSaveElementAsImageClick;
        var saveFrameAsImage = new FAMenuFlyoutItem
        {
            Text = Strings.SaveFrameAsImage,
            IconSource = new FluentIconSource
            {
                Icon = Icon.Image
            },
        };
        saveFrameAsImage.Click += OnSaveFrameAsImageClick;
        var resetZoom = new FAMenuFlyoutItem
        {
            Text = Strings.ResetZoom
        };
        resetZoom.Click += OnResetZoomClick;

        var menu = new FAMenuFlyout()
        {
            Items =
            {
                saveFrameAsImage,
                _saveElementAsImage,
                resetZoom
            }
        };
        menu.Opening += FrameContextMenuOpening;
        control.ContextFlyout = menu;
    }

    private void OnResetZoomClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PlayerViewModel viewModel)
        {
            viewModel.FrameMatrix.Value = Matrix.Identity;
            _logger.LogInformation("Zoom reset to default.");
        }
    }

    private void FrameContextMenuOpening(object? sender, EventArgs e)
    {
        if (DataContext is PlayerViewModel viewModel && viewModel.IsCameraMode.Value)
        {
            ((System.ComponentModel.CancelEventArgs)e).Cancel = true;
            return;
        }

        _saveElementAsImage.IsEnabled = _lastSelected.TryGetTarget(out _);
    }

    internal static async Task<IStorageFile?> SaveImageFilePicker(string name, IStorageProvider storage)
    {
        FilePickerSaveOptions options = SharedFilePickerOptions.SaveImage();
        options.SuggestedFileName = $"{name} {DateTime.Now:yyyy-dd-MM HHmmss}.png";
        options.SuggestedStartLocation = await storage.TryGetWellKnownFolderAsync(WellKnownFolder.Pictures);
        return await storage.SaveFilePickerAsync(options);
    }

    internal static Task SaveImage(IStorageFile file, Bitmap bitmap)
    {
        string extension = Path.GetExtension(file.Name).ToLowerInvariant();
        EncodedImageFormat format = extension switch
        {
            ".png" or "" => EncodedImageFormat.Png,
            ".jpg" or ".jpeg" => EncodedImageFormat.Jpeg,
            ".webp" => EncodedImageFormat.Webp,
            _ => throw new NotSupportedException($"{Strings.Unsupported}: {extension}"),
        };
        // Every desktop save picker hands out files with a local path.
        string path = file.TryGetLocalPath()
            ?? throw new NotSupportedException("Images can only be saved to a local file.");
        using var output = new StagedOutputFile(path);
        if (!bitmap.Save(output.TemporaryPath, format))
            throw new IOException(MessageStrings.FailedToSaveImage);
        output.Commit(CancellationToken.None);
        return Task.CompletedTask;
    }

    // Prompts for the output-resolution multiplier. Returns the chosen scale, or null on cancel.
    private static async Task<float?> PromptSaveScale(PixelSize baseSize)
    {
        using var dialogViewModel = new SaveFrameDialogViewModel(baseSize);
        var dialog = new SaveFrameDialog { DataContext = dialogViewModel };
        if (await dialog.ShowAsync() != FAContentDialogResult.Primary) return null;

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
                // Size the guard against the element's actual bounds.
                PixelSize elementSize = await viewModel.MeasureSelectedDrawable(drawable);

                // A 0x0 element cannot be rendered; report up-front.
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
                if (await PromptSaveScale(scene.FrameSize.Value) is not { } scale) return;

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
