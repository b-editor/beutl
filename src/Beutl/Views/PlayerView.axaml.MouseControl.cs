using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Beutl.Animation;
using Beutl.Controls;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Transformation;
using Beutl.Helpers;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Pixel;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.ViewModels;
using Beutl.ViewModels.Editors;
using Beutl.ViewModels.Tools;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.Logging;
using System.Numerics;
using Beutl.Graphics3D;
using Beutl.Graphics3D.Camera;
using Beutl.Operators.Source;
using AvaImage = Avalonia.Controls.Image;
using AvaPoint = Avalonia.Point;
using AvaRect = Avalonia.Rect;

namespace Beutl.Views;

public partial class PlayerView
{
    private static double Length(AvaPoint point)
    {
        return Math.Sqrt((point.X * point.X) + (point.Y * point.Y));
    }

    private sealed class KeyFrameState<T>(KeyFrame<T>? previous, KeyFrame<T>? next)
    {
        public KeyFrame<T>? Previous { get; } = previous;

        public KeyFrame<T>? Next { get; } = next;
    }

    private interface IMouseControlHandler
    {
        void OnMoved(PointerEventArgs e);

        void OnPressed(PointerPressedEventArgs e);

        void OnReleased(PointerReleasedEventArgs e);

        void OnWheelChanged(PointerWheelEventArgs e)
        {
        }

        void OnKeyDown(KeyEventArgs e)
        {
        }

        void OnKeyUp(KeyEventArgs e)
        {
        }
    }

    private class MouseControlHand : IMouseControlHandler
    {
        private bool _pressed;
        private AvaPoint _position;

        public required PlayerView View { get; init; }

        public required PlayerViewModel ViewModel { get; init; }

        private Player Player => View.Player;

        public void OnWheelChanged(PointerWheelEventArgs e)
        {
            const float ZoomSpeed = 1.2f;

            AvaPoint pos = e.GetPosition(View.framePanel);
            float x = (float)pos.X;
            float y = (float)pos.Y;
            float delta = (float)e.Delta.Y;
            float realDelta = MathF.Sign(delta) * MathF.Abs(delta);

            float ratio = MathF.Pow(ZoomSpeed, realDelta);

            var a = new Matrix(ratio, 0, 0, ratio, x - (ratio * x), y - (ratio * y));
            ViewModel.FrameMatrix.Value = a * ViewModel.FrameMatrix.Value;

            e.Handled = true;
        }

        public void OnMoved(PointerEventArgs e)
        {
            if (_pressed)
            {
                AvaPoint position = e.GetPosition(Player);
                AvaPoint delta = position - _position;
                ViewModel.FrameMatrix.Value *= Matrix.CreateTranslation((float)delta.X, (float)delta.Y);

                _position = position;

                View.framePanel.Cursor = Cursors.HandGrab;
                e.Handled = true;
            }
        }

        public void OnReleased(PointerReleasedEventArgs e)
        {
            if (_pressed)
            {
                View.framePanel.Cursor = Cursors.Hand;
                _pressed = false;
            }
        }

        public void OnPressed(PointerPressedEventArgs e)
        {
            PointerPoint pointerPoint = e.GetCurrentPoint(Player);
            _pressed = pointerPoint.Properties.IsLeftButtonPressed || pointerPoint.Properties.IsMiddleButtonPressed;
            _position = pointerPoint.Position;
            if (_pressed)
            {
                View.framePanel.Cursor = Cursors.HandGrab;

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
        private KeyFrameState<float>? _xKeyFrame;
        private KeyFrameState<float>? _yKeyFrame;

        public required PlayerView View { get; init; }

        public required PlayerViewModel ViewModel { get; init; }

        public EditViewModel EditViewModel => ViewModel.EditViewModel;

        public Drawable? Drawable { get; private set; }

        public Element? Element { get; private set; }

        private AvaImage Image => View.image;

        private (TranslateTransform?, Matrix) FindOrCreateTranslation(Drawable drawable)
        {
            switch (drawable.Transform.CurrentValue)
            {
                case TranslateTransform translateTransform:
                    return (translateTransform, Matrix.Identity);

                case TransformGroup transformGroup:
                    var list = transformGroup.Children;
                    TranslateTransform? obj = null;
                    int i;
                    for (i = 0; i < list.Count; i++)
                    {
                        Transform item = list[i];
                        if (item is TranslateTransform translate)
                        {
                            obj = translate;
                            break;
                        }
                    }

                    if (obj == null)
                    {
                        obj = new TranslateTransform();
                        transformGroup.Children.Insert(0, obj);
                        EditViewModel.HistoryManager.Commit(CommandNames.TransformElement);

                        return (obj, Matrix.Identity);
                    }
                    else
                    {
                        var res = transformGroup.ToResource(new RenderContext(EditViewModel.CurrentTime.Value));

                        return (obj, res.Matrix);
                    }
            }

            return (null, Matrix.Identity);
        }

        private KeyFrameState<float>? FindKeyFramePairOrNull(IProperty<float> property)
        {
            int rate = EditViewModel.Scene.FindHierarchicalParent<Project>() is { } proj ? proj.GetFrameRate() : 30;
            TimeSpan globalkeyTime = EditViewModel.CurrentTime.Value;
            TimeSpan localKeyTime = Element != null ? globalkeyTime - Element.Start : globalkeyTime;

            if (property.Animation is KeyFrameAnimation<float> animation)
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
                if (!ViewModel.IsMoveMode.Value)
                    return;

                PointerPoint pointerPoint = e.GetCurrentPoint(Image);
                AvaPoint imagePosition = pointerPoint.Position;
                double scaleX = Image.Bounds.Size.Width / EditViewModel.Scene.FrameSize.Width;
                AvaPoint scaledPosition = imagePosition / scaleX;
                AvaPoint delta = scaledPosition - _scaledStartPosition;
                if (_translateTransform == null && Length(delta) >= 1)
                {
                    (_translateTransform, _preMatrix) = FindOrCreateTranslation(Drawable);

                    // 最初の一回だけ、キーフレームを探す
                    if (_translateTransform != null)
                    {
                        // アニメーションが設定されていない場合の編集コマンドの復元に使うのでCurrentValueで良い
                        _xKeyFrame = FindKeyFramePairOrNull(_translateTransform.X);
                        _yKeyFrame = FindKeyFramePairOrNull(_translateTransform.Y);
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
                        _translateTransform.X.CurrentValue += (float)delta.X;
                    }

                    if (!SetKeyFrameValue(_yKeyFrame, (float)delta.Y))
                    {
                        _translateTransform.Y.CurrentValue += (float)delta.Y;
                    }
                }

                _scaledStartPosition = scaledPosition;
                if (Element != null)
                {
                    int rate = EditViewModel.Player.GetFrameRate();
                    int st = (int)Element.Start.ToFrameNumber(rate);
                    int ed = (int)Math.Ceiling(Element.Range.End.ToFrameNumber(rate));

                    EditViewModel.FrameCacheManager.Value.DeleteAndUpdateBlocks([(st, ed)]);
                }

                e.Handled = true;
            }
        }

        // keyframesが両方nullの場合、falseを返す
        private static bool SetKeyFrameValue(KeyFrameState<float>? keyframes, float delta)
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

        public void OnReleased(PointerReleasedEventArgs e)
        {
            if (_imagePressed)
            {
                _imagePressed = false;

                EditViewModel.HistoryManager.Commit(CommandNames.TransformElement);

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
            Scene scene = EditViewModel.Scene;
            PointerPoint pointerPoint = e.GetCurrentPoint(Image);
            _imagePressed = pointerPoint.Properties.IsLeftButtonPressed;
            AvaPoint imagePosition = pointerPoint.Position;
            double scaleX = Image.Bounds.Size.Width / scene.FrameSize.Width;
            _scaledStartPosition = imagePosition / scaleX;

            Drawable = RenderThread.Dispatcher.Invoke(() =>
                EditViewModel.Renderer.Value.HitTest(
                    new((float)_scaledStartPosition.X, (float)_scaledStartPosition.Y)));

            if (Drawable != null)
            {
                // TODO: DrawableGroup以下のDrawableを拾った場合の対応
                int zindex = Drawable.ZIndex;
                TimeSpan time = EditViewModel.CurrentTime.Value;

                Element = scene.Children.FirstOrDefault(v =>
                    v.ZIndex == zindex
                    && v.Start <= time
                    && time < v.Range.End);

                if (Element != null)
                {
                    EditViewModel.SelectedObject.Value = Element;
                }
            }

            e.Handled = _imagePressed;

            if (e.ClickCount == 2 && Drawable is Graphics.Shapes.Shape shape)
            {
                SourceOperatorsTabViewModel? tab = EditViewModel.FindToolTab<SourceOperatorsTabViewModel>();
                if (tab != null)
                {
                    foreach (SourceOperatorViewModel item in tab.Items)
                    {
                        IPropertyEditorContext?
                            prop = item.Properties.FirstOrDefault(v => v is GeometryEditorViewModel);
                        if (prop is GeometryEditorViewModel geometryEditorViewModel)
                        {
                            EditViewModel.Player.PathEditor.StartEdit(shape, geometryEditorViewModel,
                                _scaledStartPosition);
                            break;
                        }
                    }
                }
            }
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

        public required PlayerView View { get; init; }

        public required PlayerViewModel ViewModel { get; init; }

        private Player Player => View.Player;

        private AvaImage Image => View.image;

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
                Scene scene = ViewModel.Scene!;
                Task<Bitmap<Bgra8888>> renderTask = ViewModel.DrawFrame();

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
                float scale = ViewModel.Scene!.FrameSize.Width / (float)Image.Bounds.Width;
                Rect rect = new Rect(_start.ToBtlPoint() * scale, _position.ToBtlPoint() * scale).Normalize();

                if (ViewModel.TcsForCrop == null)
                {
                    var copyAsString = new MenuFlyoutItem()
                    {
                        Text = Strings.Copy, IconSource = new SymbolIconSource() { Symbol = Symbol.Copy }
                    };
                    var saveAsImage = new MenuFlyoutItem()
                    {
                        Text = Strings.SaveAsImage, IconSource = new SymbolIconSource() { Symbol = Symbol.SaveAs }
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
                                Task<Bitmap<Bgra8888>> renderTask = ViewModel.DrawFrame();

                                string addtional = Path.GetFileNameWithoutExtension(scene.Uri!.LocalPath);
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
                            IconSource = new SymbolIconSource() { Symbol = Symbol.ImageCopy }
                        };
                        copyAsImage.Click += (s, e) => OnCopyAsImageClicked(rect);

                        list.Add(copyAsImage);
                    }

                    list.AddRange([copyAsString, saveAsImage]);

                    var f = new FAMenuFlyout { ItemsSource = list };

                    f.ShowAt(Player, true);
                }
                else
                {
                    ViewModel.TcsForCrop?.SetResult(rect);
                }

                ViewModel.LastSelectedRect = rect;

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

    private sealed class MouseControl3DCamera : IMouseControlHandler
    {
        private bool _rightPressed;
        private AvaPoint _lastPosition;
        private Scene3D? _scene3D;
        private Camera3D? _camera;
        private float _yaw;
        private float _pitch;
        private readonly HashSet<Key> _pressedKeys = [];

        private const float RotationSpeed = 0.005f;
        private const float MoveSpeed = 0.1f;

        public required PlayerView View { get; init; }

        public required PlayerViewModel ViewModel { get; init; }

        public EditViewModel EditViewModel => ViewModel.EditViewModel;

        private AvaImage Image => View.image;

        public void OnPressed(PointerPressedEventArgs e)
        {
            PointerPoint pointerPoint = e.GetCurrentPoint(Image);
            if (pointerPoint.Properties.IsRightButtonPressed)
            {
                _rightPressed = true;
                _lastPosition = pointerPoint.Position;

                // カメラとシーンを見つける
                FindScene3DAndCamera();

                if (_camera != null)
                {
                    // カメラの方向からYawとPitchを計算する
                    var position = _camera.Position.CurrentValue;
                    var target = _camera.Target.CurrentValue;
                    var forward = Vector3.Normalize(target - position);

                    _yaw = MathF.Atan2(forward.X, forward.Z);
                    _pitch = MathF.Asin(-forward.Y);
                }

                e.Handled = true;
            }
        }

        public void OnMoved(PointerEventArgs e)
        {
            if (_rightPressed && _camera != null)
            {
                AvaPoint position = e.GetPosition(Image);
                AvaPoint delta = position - _lastPosition;

                // マウスの動きに応じてYawとPitchを更新
                _yaw += (float)delta.X * RotationSpeed;
                _pitch += (float)delta.Y * RotationSpeed;

                _pitch = Math.Clamp(_pitch, (-MathF.PI / 2) + 0.1f, (MathF.PI / 2) - 0.1f);

                // 新しいforward directionを計算する
                var forward = new Vector3(
                    MathF.Sin(_yaw) * MathF.Cos(_pitch),
                    -MathF.Sin(_pitch),
                    MathF.Cos(_yaw) * MathF.Cos(_pitch)
                );

                // カメラのターゲットを更新する
                var cameraPosition = _camera.Position.CurrentValue;
                _camera.Target.CurrentValue = cameraPosition + forward;

                _lastPosition = position;
                e.Handled = true;
            }
        }

        public void OnReleased(PointerReleasedEventArgs e)
        {
            if (_rightPressed && e.InitialPressMouseButton == MouseButton.Right)
            {
                _rightPressed = false;
                EditViewModel.HistoryManager.Commit(CommandNames.TransformElement);
            }
        }

        public void OnWheelChanged(PointerWheelEventArgs e)
        {
            if (_camera != null)
            {
                var position = _camera.Position.CurrentValue;
                var target = _camera.Target.CurrentValue;
                var forward = Vector3.Normalize(target - position);

                float speed = (float)e.Delta.Y * MoveSpeed * 3;
                position += forward * speed;
                target += forward * speed;

                _camera.Position.CurrentValue = position;
                _camera.Target.CurrentValue = target;

                EditViewModel.HistoryManager.Commit(CommandNames.TransformElement);
                e.Handled = true;
            }
        }

        public void OnKeyDown(KeyEventArgs e)
        {
            if (!_rightPressed || _camera == null)
                return;

            _pressedKeys.Add(e.Key);
            ProcessMovement();
            e.Handled = true;
        }

        public void OnKeyUp(KeyEventArgs e)
        {
            _pressedKeys.Remove(e.Key);
        }

        private void ProcessMovement()
        {
            if (_camera == null)
                return;

            var position = _camera.Position.CurrentValue;
            var target = _camera.Target.CurrentValue;
            var forward = Vector3.Normalize(target - position);
            var up = _camera.Up.CurrentValue;
            var right = Vector3.Normalize(Vector3.Cross(forward, up));

            var movement = Vector3.Zero;

            foreach (Key key in _pressedKeys)
            {
                switch (key)
                {
                    case Key.W:
                        movement += forward * MoveSpeed;
                        break;
                    case Key.S:
                        movement -= forward * MoveSpeed;
                        break;
                    case Key.A:
                        movement -= right * MoveSpeed;
                        break;
                    case Key.D:
                        movement += right * MoveSpeed;
                        break;
                    case Key.E:
                        movement += up * MoveSpeed;
                        break;
                    case Key.Q:
                        movement -= up * MoveSpeed;
                        break;
                }
            }

            if (movement != Vector3.Zero)
            {
                position += movement;
                target += movement;

                _camera.Position.CurrentValue = position;
                _camera.Target.CurrentValue = target;
            }
        }

        private void FindScene3DAndCamera()
        {
            _scene3D = null;
            _camera = null;

            // 選択されているオブジェクトから探す
            if (EditViewModel.SelectedObject.Value is Element element)
            {
                foreach (var op in element.Operation.Children)
                {
                    if (op is Scene3DOperator scene3DOp)
                    {
                        _scene3D = scene3DOp.Value;
                        _camera = _scene3D.Camera.CurrentValue;
                        return;
                    }
                }
            }

            // マウス位置から探す
            Scene scene = EditViewModel.Scene;
            AvaPoint pos = _lastPosition;
            double scaleX = Image.Bounds.Size.Width / scene.FrameSize.Width;
            var scaledPos = pos / scaleX;

            var drawable = RenderThread.Dispatcher.Invoke(() =>
                EditViewModel.Renderer.Value.HitTest(
                    new((float)scaledPos.X, (float)scaledPos.Y)));

            if (drawable is Scene3D scene3D)
            {
                _scene3D = scene3D;
                _camera = scene3D.Camera.CurrentValue;
            }
        }
    }

    private readonly WeakReference<Drawable?> _lastSelected = new(null);
    private IMouseControlHandler? _mouseState;
    private int _lastMouseMode = -1;

    private int GetMouseModeIndex(PlayerViewModel viewModel)
    {
        if (viewModel.IsMoveMode.Value)
        {
            return 0;
        }
        else if (viewModel.IsHandMode.Value)
        {
            return 1;
        }
        else if (viewModel.IsCropMode.Value)
        {
            return 2;
        }
        else if (viewModel.IsCameraMode.Value)
        {
            return 3;
        }
        else
        {
            return -1;
        }
    }

    private void SetMouseMode(PlayerViewModel viewModel, int index, bool value)
    {
        switch (index)
        {
            case 0:
                viewModel.IsMoveMode.Value = value;
                break;
            case 1:
                viewModel.IsHandMode.Value = value;
                break;
            case 2:
                viewModel.IsCropMode.Value = value;
                break;
            case 3:
                viewModel.IsCameraMode.Value = value;
                break;
        }
    }

    private void OnFramePointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is PlayerViewModel viewModel)
        {
            CreateMouseHandler(viewModel).OnWheelChanged(e);
        }
    }

    private void OnFrameKeyDown(object? sender, KeyEventArgs e)
    {
        _mouseState?.OnKeyDown(e);
    }

    private void OnFrameKeyUp(object? sender, KeyEventArgs e)
    {
        _mouseState?.OnKeyUp(e);
    }

    private void OnFramePointerMoved(object? sender, PointerEventArgs e)
    {
        _mouseState?.OnMoved(e);
    }

    private void OnFramePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _mouseState?.OnReleased(e);
        _mouseState = null;

        if (DataContext is PlayerViewModel viewModel
            && e.InitialPressMouseButton == MouseButton.Middle)
        {
            SetMouseMode(viewModel, _lastMouseMode, true);

            _lastMouseMode = -1;
        }
    }

    private IMouseControlHandler CreateMouseHandler(PlayerViewModel viewModel)
    {
        if (viewModel.IsMoveMode.Value)
        {
            return new MouseControlMove { ViewModel = viewModel, View = this };
        }
        else if (viewModel.IsHandMode.Value)
        {
            return new MouseControlHand { ViewModel = viewModel, View = this };
        }
        else if (viewModel.IsCameraMode.Value)
        {
            return new MouseControl3DCamera { ViewModel = viewModel, View = this };
        }
        else
        {
            return new MouseControlCrop { ViewModel = viewModel, View = this };
        }
    }

    private void OnFramePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(null);
        if (DataContext is PlayerViewModel viewModel)
        {
            if (viewModel.IsCameraMode.Value)
            {
                if (point.Properties.IsLeftButtonPressed || point.Properties.IsRightButtonPressed)
                {
                    _mouseState = CreateMouseHandler(viewModel);
                    _mouseState.OnPressed(e);
                    framePanel.Focus();
                }

                return;
            }

            if (point.Properties.IsLeftButtonPressed || point.Properties.IsMiddleButtonPressed)
            {
                if (point.Properties.IsMiddleButtonPressed)
                {
                    _lastMouseMode = GetMouseModeIndex(viewModel);
                    viewModel.IsHandMode.Value = true;
                }

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
}
