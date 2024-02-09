using System.Collections.Immutable;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;

using Beutl.Animation;
using Beutl.Commands;
using Beutl.Controls;
using Beutl.Graphics;
using Beutl.Graphics.Transformation;
using Beutl.Helpers;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Pixel;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.ViewModels;

using FluentAvalonia.UI.Controls;

using Microsoft.Extensions.Logging;

using AvaImage = Avalonia.Controls.Image;
using AvaPoint = Avalonia.Point;
using AvaRect = Avalonia.Rect;

namespace Beutl.Views;

public partial class EditView
{
    private static double Length(AvaPoint point)
    {
        return Math.Sqrt((point.X * point.X) + (point.Y * point.Y));
    }

    private sealed class KeyFrameState(KeyFrame<float>? previous, KeyFrame<float>? next)
    {
        public KeyFrame<float>? Previous { get; } = previous;

        public KeyFrame<float>? Next { get; } = next;

        public float OldPreviousValue { get; } = previous?.Value ?? 0;

        public float OldNextValue { get; } = next?.Value ?? 0;

        public IRecordableCommand? CreateCommand(ImmutableArray<IStorable?> storables)
        {
            return RecordableCommands.Append(
                Previous != null && Previous.Value != OldPreviousValue
                    ? RecordableCommands.Edit(Previous, KeyFrame<float>.ValueProperty, Previous.Value, OldPreviousValue).WithStoables(storables)
                    : null,
                Next != null && Next.Value != OldNextValue
                    ? RecordableCommands.Edit(Next, KeyFrame<float>.ValueProperty, Next.Value, OldNextValue).WithStoables(storables)
                    : null);
        }
    }

    private interface IMouseControlHandler
    {
        void OnMoved(PointerEventArgs e);

        void OnPressed(PointerPressedEventArgs e);

        void OnReleased(PointerReleasedEventArgs e);

        void OnWheelChanged(PointerWheelEventArgs e)
        {
        }
    }

    private class MouseControlHand : IMouseControlHandler
    {
        private bool _pressed;
        private AvaPoint _position;

        public required Player Player { get; init; }

        public required AvaImage Image { get; init; }

        public required EditViewModel viewModel { get; init; }

        public void OnWheelChanged(PointerWheelEventArgs e)
        {
            const float ZoomSpeed = 1.2f;

            AvaPoint pos = e.GetPosition(Image);
            float x = (float)pos.X;
            float y = (float)pos.Y;
            float delta = (float)e.Delta.Y;
            float realDelta = MathF.Sign(delta) * MathF.Abs(delta);

            float ratio = MathF.Pow(ZoomSpeed, realDelta);

            var a = new Matrix(ratio, 0, 0, ratio, x - (ratio * x), y - (ratio * y));
            viewModel.Player.FrameMatrix.Value = a * viewModel.Player.FrameMatrix.Value;

            e.Handled = true;
        }

        public void OnMoved(PointerEventArgs e)
        {
            if (_pressed)
            {
                AvaPoint position = e.GetPosition(Player);
                AvaPoint delta = position - _position;
                viewModel.Player.FrameMatrix.Value *= Matrix.CreateTranslation((float)delta.X, (float)delta.Y);

                _position = position;

                e.Handled = true;
            }
        }

        public void OnReleased(PointerReleasedEventArgs e)
        {
            if (_pressed)
            {
                Player.GetFramePanel().Cursor = Cursors.Hand;
                _pressed = false;
            }
        }

        public void OnPressed(PointerPressedEventArgs e)
        {
            PointerPoint pointerPoint = e.GetCurrentPoint(Player);
            _pressed = pointerPoint.Properties.IsLeftButtonPressed;
            _position = pointerPoint.Position;
            if (_pressed)
            {
                Player.GetFramePanel().Cursor = Cursors.HandGrab;

                e.Handled = true;
            }
        }
    }

    private sealed class MouseControlMove : IMouseControlHandler
    {
        private bool _imagePressed;
        private AvaPoint _scaledStartPosition;
        private TranslateTransform? _translateTransform;
        private Matrix _preMatrix = Matrix.Identity;
        private Point _oldTranslation;
        private KeyFrameState? _xKeyFrame;
        private KeyFrameState? _yKeyFrame;

        public required AvaImage Image { get; init; }

        public required EditViewModel viewModel { get; init; }

        public Drawable? Drawable { get; private set; }

        public Element? Element { get; private set; }

        private (TranslateTransform?, Matrix) FindOrCreateTranslation(Drawable drawable)
        {
            switch (drawable.Transform)
            {
                case TranslateTransform translateTransform:
                    return (translateTransform, Matrix.Identity);

                case TransformGroup transformGroup:
                    Transforms list = transformGroup.Children;
                    TranslateTransform? obj = null;
                    int i;
                    for (i = 0; i < list.Count; i++)
                    {
                        ITransform item = list[i];
                        if (item is TranslateTransform translate)
                        {
                            obj = translate;
                            break;
                        }
                    }

                    if (obj == null)
                    {
                        obj = new TranslateTransform();
                        transformGroup.Children.BeginRecord<ITransform>()
                            .Insert(0, obj)
                            .ToCommand([Element])
                            .DoAndRecord(viewModel.CommandRecorder);

                        return (obj, Matrix.Identity);
                    }
                    else
                    {
                        Matrix matrix = Matrix.Identity;
                        for (int j = 0; j < i; j++)
                        {
                            ITransform item = list[j];
                            if (item.IsEnabled)
                                matrix = list[j].Value * matrix;
                        }

                        return (obj, matrix);
                    }
            }

            return (null, Matrix.Identity);
        }

        private KeyFrameState? FindKeyFramePairOrNull(CoreProperty<float> property)
        {
            int rate = viewModel.Scene.FindHierarchicalParent<Project>() is { } proj ? proj.GetFrameRate() : 30;
            TimeSpan globalkeyTime = viewModel.CurrentTime.Value;
            TimeSpan localKeyTime = Element != null ? globalkeyTime - Element.Start : globalkeyTime;

            if (_translateTransform!.Animations.FirstOrDefault(v => v.Property == property) is KeyFrameAnimation<float> animation)
            {
                TimeSpan keyTime = animation.UseGlobalClock ? globalkeyTime : localKeyTime;
                keyTime = keyTime.RoundToRate(rate);

                (IKeyFrame? prev, IKeyFrame? next) = animation.KeyFrames.GetPreviousAndNextKeyFrame(keyTime);

                if (next?.KeyTime == keyTime)
                    return new(next as KeyFrame<float>, null);

                return new(prev as KeyFrame<float>, next as KeyFrame<float>);
            }

            return default;
        }

        public void OnMoved(PointerEventArgs e)
        {
            if (_imagePressed && Drawable != null)
            {
                if (!viewModel.Player.IsMoveMode.Value)
                    return;

                PointerPoint pointerPoint = e.GetCurrentPoint(Image);
                AvaPoint imagePosition = pointerPoint.Position;
                double scaleX = Image.Bounds.Size.Width / viewModel.Scene.FrameSize.Width;
                AvaPoint scaledPosition = imagePosition / scaleX;
                AvaPoint delta = scaledPosition - _scaledStartPosition;
                if (_translateTransform == null && Length(delta) >= 1)
                {
                    (_translateTransform, _preMatrix) = FindOrCreateTranslation(Drawable);

                    // 最初の一回だけ、キーフレームを探す
                    if (_translateTransform != null)
                    {
                        _oldTranslation = new(_translateTransform.X, _translateTransform.Y);
                        _xKeyFrame = FindKeyFramePairOrNull(TranslateTransform.XProperty);
                        _yKeyFrame = FindKeyFramePairOrNull(TranslateTransform.YProperty);
                    }
                }
                if (_preMatrix.TryInvert(out Matrix inverted))
                {
                    Avalonia.Matrix avaInverted = inverted.ToAvaMatrix();
                    AvaPoint scaledPosition1 = scaledPosition * avaInverted;
                    AvaPoint scaledStartPosition1 = _scaledStartPosition * avaInverted;
                    delta = scaledPosition1 - scaledStartPosition1;
                }

                if (_translateTransform != null)
                {
                    if (!SetKeyFrameValue(_xKeyFrame, (float)delta.X))
                    {
                        _translateTransform.X += (float)delta.X;
                    }

                    if (!SetKeyFrameValue(_yKeyFrame, (float)delta.Y))
                    {
                        _translateTransform.Y += (float)delta.Y;
                    }
                }

                _scaledStartPosition = scaledPosition;
                if (Element != null)
                {
                    int rate = viewModel.Player.GetFrameRate();
                    int st = (int)Element.Start.ToFrameNumber(rate);
                    int ed = (int)Math.Ceiling(Element.Range.End.ToFrameNumber(rate));

                    viewModel.FrameCacheManager.Value.DeleteAndUpdateBlocks(new[] { (st, ed) });
                }
                e.Handled = true;
            }
        }

        // keyframesが両方nullの場合、falseを返す
        private static bool SetKeyFrameValue(KeyFrameState? keyframes, float delta)
        {
            switch ((keyframes?.Previous, keyframes?.Next))
            {
                case (null, null):
                    return false;

                case ({ } prev, { } next):
                    prev.Value += delta;
                    next.Value += delta;
                    break;

                case ({ } prev, null):
                    prev.Value += delta;
                    break;

                case (null, { } next):
                    next.Value += delta;
                    break;
            }

            return true;
        }

        private IRecordableCommand? CreateTranslationCommand(ImmutableArray<IStorable?> storables)
        {
            if (_translateTransform != null)
            {
                return RecordableCommands.Append(
                    _translateTransform.X != _oldTranslation.X
                        ? RecordableCommands.Edit(_translateTransform, TranslateTransform.XProperty, _translateTransform.X, _oldTranslation.X).WithStoables(storables)
                        : null,
                    _translateTransform.Y != _oldTranslation.Y
                        ? RecordableCommands.Edit(_translateTransform, TranslateTransform.YProperty, _translateTransform.Y, _oldTranslation.Y).WithStoables(storables)
                        : null);
            }

            return null;
        }

        public void OnReleased(PointerReleasedEventArgs e)
        {
            if (_imagePressed)
            {
                _imagePressed = false;

                ImmutableArray<IStorable?> storables = [Element];
                IRecordableCommand? command = CreateTranslationCommand(storables)
                    .Append((_xKeyFrame?.CreateCommand(storables)).Append(_yKeyFrame?.CreateCommand(storables)));
                command?.DoAndRecord(viewModel.CommandRecorder);

                Element = null;
                _translateTransform = null;
                Drawable = null;
                _xKeyFrame = default;
                _yKeyFrame = default;
                e.Handled = true;
            }
        }

        public void OnPressed(PointerPressedEventArgs e)
        {
            Scene scene = viewModel.Scene;
            PointerPoint pointerPoint = e.GetCurrentPoint(Image);
            _imagePressed = pointerPoint.Properties.IsLeftButtonPressed;
            AvaPoint imagePosition = pointerPoint.Position;
            double scaleX = Image.Bounds.Size.Width / scene.FrameSize.Width;
            _scaledStartPosition = imagePosition / scaleX;

            Drawable = viewModel.Renderer.Value.HitTest(new((float)_scaledStartPosition.X, (float)_scaledStartPosition.Y));

            if (Drawable != null)
            {
                int zindex = (Drawable as DrawableDecorator)?.OriginalZIndex ?? Drawable.ZIndex;
                TimeSpan time = viewModel.CurrentTime.Value;

                Element = scene.Children.FirstOrDefault(v =>
                    v.ZIndex == zindex
                    && v.Start <= time
                    && time < v.Range.End);

                if (Element != null)
                {
                    viewModel.SelectedObject.Value = Element;
                }
            }

            e.Handled = _imagePressed;
        }
    }

    private sealed class MouseControlCrop : IMouseControlHandler
    {
        private readonly ILogger _logger = Log.CreateLogger<MouseControlCrop>();
        private bool _pressed;
        private AvaPoint _start;
        private AvaPoint _position;
        private AvaPoint _startInPanel;
        private AvaPoint _positionInPanel;
        private Border? _border;

        public required Player Player { get; init; }

        public required AvaImage Image { get; init; }

        public required EditViewModel ViewModel { get; init; }

        public void OnMoved(PointerEventArgs e)
        {
            if (_pressed)
            {
                _position = e.GetPosition(Image);
                _positionInPanel = e.GetPosition(Player.GetFramePanel());
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

        private static Bitmap<Bgra8888> CropFrame(Bitmap<Bgra8888> frame, Rect rect)
        {
            var pxRect = PixelRect.FromRect(rect);
            var bounds = new PixelRect(0, 0, frame.Width, frame.Height);
            if (bounds.Contains(pxRect))
            {
                return frame[pxRect];
            }
            else
            {
                PixelRect intersect = bounds.Intersect(pxRect);
                using Bitmap<Bgra8888> intersectBitmap = frame[intersect];
                var result = new Bitmap<Bgra8888>(pxRect.Width, pxRect.Height);

                PixelPoint leftTop = intersect.Position - pxRect.Position;
                result[new PixelRect(leftTop.X, leftTop.Y, intersect.Width, intersect.Height)] = intersectBitmap;

                return result;
            }
        }

        private async void OnCopyAsImageClicked(Rect rect)
        {
            try
            {
                EditViewModel viewModel = ViewModel;
                Scene scene = ViewModel.Scene;
                Task<Bitmap<Bgra8888>> renderTask = viewModel.Player.DrawFrame();

                FilePickerSaveOptions options = SharedFilePickerOptions.SaveImage();

                using Bitmap<Bgra8888> frame = await renderTask;
                using Bitmap<Bgra8888> croped = CropFrame(frame, rect);

                WindowsClipboard.CopyImage(croped);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save image.");
                NotificationService.ShowError(Message.Failed_to_save_image, ex.Message);
            }
        }

        public void OnReleased(PointerReleasedEventArgs e)
        {
            if (_pressed)
            {
                float scale = ViewModel.Scene.FrameSize.Width / (float)Image.Bounds.Width;
                Rect rect = new Rect(_start.ToBtlPoint() * scale, _position.ToBtlPoint() * scale).Normalize();

                if (ViewModel.Player.TcsForCrop == null)
                {
                    var copyAsString = new MenuFlyoutItem()
                    {
                        Text = Strings.Copy,
                        IconSource = new SymbolIconSource()
                        {
                            Symbol = Symbol.Copy
                        }
                    };
                    var saveAsImage = new MenuFlyoutItem()
                    {
                        Text = Strings.SaveAsImage,
                        IconSource = new SymbolIconSource()
                        {
                            Symbol = Symbol.SaveAs
                        }
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
                                EditViewModel viewModel = ViewModel;
                                Scene scene = ViewModel.Scene;
                                Task<Bitmap<Bgra8888>> renderTask = viewModel.Player.DrawFrame();

                                FilePickerSaveOptions options = SharedFilePickerOptions.SaveImage();
                                string addtional = Path.GetFileNameWithoutExtension(scene.FileName);
                                IStorageFile? file = await SaveImageFilePicker(addtional, storage);

                                if (file != null)
                                {
                                    using Bitmap<Bgra8888> frame = await renderTask;
                                    using Bitmap<Bgra8888> croped = CropFrame(frame, rect);

                                    await SaveImage(file, croped);
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Failed to save image.");
                                NotificationService.ShowError(Message.Failed_to_save_image, ex.Message);
                            }
                        }
                    };

                    var list = new List<MenuFlyoutItem>();
                    if (OperatingSystem.IsWindows())
                    {
                        var copyAsImage = new MenuFlyoutItem()
                        {
                            Text = Strings.CopyAsImage,
                            IconSource = new SymbolIconSource()
                            {
                                Symbol = Symbol.ImageCopy
                            }
                        };
                        copyAsImage.Click += (s, e) => OnCopyAsImageClicked(rect);

                        list.Add(copyAsImage);
                    }
                    list.AddRange([copyAsString, saveAsImage]);

                    var f = new FAMenuFlyout
                    {
                        ItemsSource = list
                    };

                    f.ShowAt(Player, true);
                }
                else
                {
                    ViewModel.Player.TcsForCrop?.SetResult(rect);
                }

                ViewModel.Player.LastSelectedRect = rect;

                if (_border != null)
                {
                    Player.GetFramePanel().Children.Remove(_border);
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
            Panel panel = Player.GetFramePanel();
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

    private readonly WeakReference<Drawable?> _lastSelected = new(null);
    private IMouseControlHandler? _mouseState;

    private void OnFramePointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is EditViewModel viewModel)
        {
            CreateMouseHandler(viewModel).OnWheelChanged(e);
        }
    }

    private void OnFramePointerMoved(object? sender, PointerEventArgs e)
    {
        _mouseState?.OnMoved(e);
    }

    private void OnFramePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _mouseState?.OnReleased(e);
        _mouseState = null;
    }

    private IMouseControlHandler CreateMouseHandler(EditViewModel viewModel)
    {
        if (viewModel.Player.IsMoveMode.Value)
        {
            return new MouseControlMove
            {
                Image = Image,
                viewModel = viewModel
            };
        }
        else if (viewModel.Player.IsHandMode.Value)
        {
            return new MouseControlHand
            {
                Player = Player,
                Image = Image,
                viewModel = viewModel
            };
        }
        else
        {
            return new MouseControlCrop
            {
                Player = Player,
                Image = Image,
                ViewModel = viewModel
            };
        }
    }

    private void OnFramePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is EditViewModel viewModel)
        {
            _mouseState = CreateMouseHandler(viewModel);

            _mouseState.OnPressed(e);
            // Todo: 抽象化する
            if (_mouseState is MouseControlMove move)
            {
                _lastSelected.SetTarget(move.Drawable);
            }
        }
    }
}
