using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Beutl.Controls;
using Beutl.Editor.Components.Views;
using Beutl.Graphics;
using Beutl.Helpers;
using Beutl.Logging;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.ViewModels;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.Logging;
using AvaPoint = Avalonia.Point;
using AvaRect = Avalonia.Rect;
using FluentIconSource = FluentIcons.Avalonia.Fluent.FluentIconSource;
using Icon = FluentIcons.Common.Icon;

namespace Beutl.Views;

public partial class PlayerView
{
    private sealed class MouseControlCrop : IMouseControlHandler
    {
        private readonly ILogger _logger = Log.CreateLogger<MouseControlCrop>();
        private bool _pressed;
        private AvaPoint _start;
        private AvaPoint _position;
        private AvaPoint _startInPanel;
        private AvaPoint _positionInPanel;
        private Border? _border;

        public required PlayerView View { get; init; }

        public required PlayerViewModel ViewModel { get; init; }

        private Player Player => View.Player;

        private Control Image => View.image;

        public void OnMoved(PointerEventArgs e)
        {
            if (_pressed)
            {
                _position = e.GetPosition(Image);
                _positionInPanel = e.GetPosition(View.framePanel);
                if (_border != null)
                {
                    AvaRect rect = new AvaRect(_startInPanel, _positionInPanel).Normalize();
                    _border.Margin = new(rect.X, rect.Y, 0, 0);
                    _border.Width = rect.Width;
                    _border.Height = rect.Height;
                }

                e.Handled = true;
            }
        }

        private static Bitmap CropFrame(Bitmap frame, Rect rect)
        {
            var pxRect = PixelRect.FromRect(rect);
            var bounds = new PixelRect(0, 0, frame.Width, frame.Height);
            if (bounds.Contains(pxRect))
            {
                return frame.ExtractSubset(pxRect);
            }
            else
            {
                PixelRect intersect = bounds.Intersect(pxRect);
                using Bitmap intersectBitmap = frame.ExtractSubset(intersect);
                var result = new Bitmap(
                    pxRect.Width, pxRect.Height,
                    intersectBitmap.ColorType, intersectBitmap.AlphaType, intersectBitmap.ColorSpace);

                PixelPoint leftTop = intersect.Position - pxRect.Position;
                result.CopyFrom(intersectBitmap, new PixelRect(leftTop.X, leftTop.Y, intersect.Width, intersect.Height));

                return result;
            }
        }

        private async void OnCopyAsImageClicked(Rect rect)
        {
            try
            {
                // Render at full scale to avoid baking preview quality into the clipboard.
                using Bitmap frame = await ViewModel.DrawFrameAtFullScale();
                using Bitmap croped = CropFrame(frame, rect);

                WindowsClipboard.CopyImage(croped);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save image.");
                NotificationService.ShowError(MessageStrings.FailedToSaveImage, ex.Message);
            }
        }

        private void ShowCropResultMenu(Rect rect)
        {
            var copyAsString = new FAMenuFlyoutItem()
            {
                Text = Strings.Copy,
                IconSource = new FluentIconSource() { Icon = Icon.Copy }
            };
            var saveAsImage = new FAMenuFlyoutItem()
            {
                Text = Strings.SaveAsImage,
                IconSource = new FluentIconSource() { Icon = Icon.SaveImage }
            };
            copyAsString.Click += (s, e) =>
            {
                if (TopLevel.GetTopLevel(Player) is { Clipboard: { } clipboard })
                {
                    clipboard.SetTextAsync(rect.ToString());
                }
            };
            saveAsImage.Click += async (s, e) =>
            {
                if (TopLevel.GetTopLevel(Player)?.StorageProvider is { } storage)
                {
                    try
                    {
                        Scene scene = ViewModel.Scene!;
                        string addtional = Path.GetFileNameWithoutExtension(scene.Uri!.LocalPath);
                        IStorageFile? file = await SaveImageFilePicker(addtional, storage);

                        if (file != null)
                        {
                            using Bitmap frame = await ViewModel.DrawFrameAtFullScale();
                            using Bitmap croped = CropFrame(frame, rect);

                            await SaveImage(file, croped);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to save image.");
                        NotificationService.ShowError(MessageStrings.FailedToSaveImage, ex.Message);
                    }
                }
            };

            var list = new List<FAMenuFlyoutItem>();
            if (OperatingSystem.IsWindows())
            {
                var copyAsImage = new FAMenuFlyoutItem()
                {
                    Text = Strings.CopyAsImage,
                    IconSource = new FluentIconSource() { Icon = Icon.ImageCopy }
                };
                copyAsImage.Click += (s, e) => OnCopyAsImageClicked(rect);

                list.Add(copyAsImage);
            }

            list.AddRange([copyAsString, saveAsImage]);

            var f = new FAMenuFlyout { ItemsSource = list };

            f.ShowAt(Player, true);
        }

        public void OnReleased(PointerReleasedEventArgs e)
        {
            if (_pressed)
            {
                float scale = ViewModel.Scene!.FrameSize.Width / (float)Image.Bounds.Width;
                Rect rect = new Rect(_start.ToBtlPoint() * scale, _position.ToBtlPoint() * scale).Normalize();

                ShowCropResultMenu(rect);

                if (_border != null)
                {
                    View.framePanel.Children.Remove(_border);
                    _border = null;
                }

                _pressed = false;
            }
        }

        public void OnPressed(PointerPressedEventArgs e)
        {
            PointerPoint pointerPoint = e.GetCurrentPoint(Image);
            _pressed = pointerPoint.Properties.IsLeftButtonPressed;
            _start = pointerPoint.Position;
            Panel panel = View.framePanel;
            _startInPanel = e.GetCurrentPoint(panel).Position;
            if (_pressed)
            {
                _border = panel.Children.OfType<Border>().FirstOrDefault(x => x.Tag is nameof(MouseControlCrop));
                if (_border == null)
                {
                    _border = new()
                    {
                        Tag = nameof(MouseControlCrop),
                        BorderBrush = TimelineSharedObject.SelectionPen.Brush,
                        BorderThickness = new(0.5),
                        Background = TimelineSharedObject.SelectionFillBrush,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top
                    };
                    panel.Children.Add(_border);
                }

                e.Handled = true;
            }
        }
    }
}
