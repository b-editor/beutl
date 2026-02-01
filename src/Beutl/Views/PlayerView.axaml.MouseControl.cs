using System.Numerics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Beutl.Animation;
using Beutl.Controls;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Transformation;
using Beutl.Graphics3D;
using Beutl.Graphics3D.Camera;
using Beutl.Graphics3D.Gizmo;
using Beutl.Helpers;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Pixel;
using Beutl.Operators.Source;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.ViewModels;
using Beutl.ViewModels.Editors;
using Beutl.ViewModels.Tools;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.Logging;
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
                        Text = Strings.Copy,
                        IconSource = new SymbolIconSource() { Symbol = Symbol.Copy }
                    };
                    var saveAsImage = new MenuFlyoutItem()
                    {
                        Text = Strings.SaveAsImage,
                        IconSource = new SymbolIconSource() { Symbol = Symbol.SaveAs }
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
        private bool _leftPressed;
        private AvaPoint _lastPosition;
        private Scene3D? _scene3D;
        private Camera3D? _camera;
        private float _yaw;
        private float _pitch;
        private readonly HashSet<Key> _pressedKeys = [];
        private DispatcherTimer? _movementTimer;
        private KeyFrameState<Vector3>? _positionKeyFrame;
        private KeyFrameState<Vector3>? _targetKeyFrame;

        // Left button object manipulation
        private Object3D? _selectedObject;
        private KeyFrameState<Vector3>? _objectPositionKeyFrame;
        private KeyFrameState<Vector3>? _objectRotationKeyFrame;
        private KeyFrameState<Vector3>? _objectScaleKeyFrame;
        private GizmoMode _currentGizmoMode;
        private GizmoAxis _selectedGizmoAxis;

        private const float RotationSpeed = 0.005f;
        private const float MoveSpeed = 0.1f;
        private const float ObjectMoveSpeed = 0.01f;
        private const float ObjectRotateSpeed = 0.5f;
        private const float ObjectScaleSpeed = 0.01f;

        public required PlayerView View { get; init; }

        public required PlayerViewModel ViewModel { get; init; }

        public EditViewModel EditViewModel => ViewModel.EditViewModel;

        private RenderContext RenderContext => field ??= new(EditViewModel.CurrentTime.Value);

        private AvaImage Image => View.image;

        private KeyFrameState<Vector3>? FindKeyFramePairOrNull(IProperty<Vector3> property)
        {
            int rate = EditViewModel.Scene.FindHierarchicalParent<Project>() is { } proj ? proj.GetFrameRate() : 30;
            TimeSpan globalKeyTime = EditViewModel.CurrentTime.Value;
            TimeSpan localKeyTime = _scene3D != null ? globalKeyTime - _scene3D.TimeRange.Start : globalKeyTime;

            if (property.Animation is KeyFrameAnimation<Vector3> animation)
            {
                TimeSpan keyTime = animation.UseGlobalClock ? globalKeyTime : localKeyTime;
                keyTime = keyTime.RoundToRate(rate);

                (IKeyFrame? prev, IKeyFrame? next) = animation.KeyFrames.GetPreviousAndNextKeyFrame(keyTime);

                if (next?.KeyTime == keyTime)
                    return new(next as KeyFrame<Vector3>, null);

                return new(prev as KeyFrame<Vector3>, next as KeyFrame<Vector3>);
            }

            return null;
        }

        // キーフレームがない場合はfalseを返す
        private static bool SetKeyFrameValue(KeyFrameState<Vector3>? keyframes, Vector3 delta)
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

        public void OnPressed(PointerPressedEventArgs e)
        {
            PointerPoint pointerPoint = e.GetCurrentPoint(Image);
            _lastPosition = pointerPoint.Position;

            // カメラとシーンを見つける
            FindScene3DAndCamera();

            if (pointerPoint.Properties.IsLeftButtonPressed)
            {
                _leftPressed = true;
                _selectedGizmoAxis = GizmoAxis.None;

                if (_scene3D == null)
                    return;

                var sceneResource = FindScene3DResource();
                if (sceneResource?.Renderer == null)
                    return;

                Scene scene = EditViewModel.Scene;
                double scaleX = Image.Bounds.Size.Width / scene.FrameSize.Width;
                var scaledPos = _lastPosition / scaleX;
                var screenPoint = new Point((float)scaledPos.X, (float)scaledPos.Y);

                // まず、既存のGizmoがクリックされたかチェック
                var currentGizmoTarget = _scene3D.GizmoTarget.CurrentValue;
                var currentGizmoMode = _scene3D.GizmoMode.CurrentValue;

                if (currentGizmoTarget.HasValue && currentGizmoMode != GizmoMode.None)
                {
                    // 現在表示されているGizmoのターゲットオブジェクトを探す
                    var existingTarget = RenderThread.Dispatcher.Invoke(() =>
                    {
                        var objects = sceneResource.Objects.Where(o => o.IsEnabled).ToList();
                        return objects.FirstOrDefault(o => o.GetOriginal()?.Id == currentGizmoTarget.Value);
                    });

                    if (existingTarget != null)
                    {
                        // GizmoのヒットテストをRenderThreadで実行
                        var gizmoAxis = RenderThread.Dispatcher.Invoke(() =>
                            sceneResource.Renderer.GizmoHitTest(screenPoint, existingTarget, currentGizmoMode));

                        if (gizmoAxis != GizmoAxis.None)
                        {
                            // Gizmoがクリックされた - そのオブジェクトを操作開始
                            _selectedGizmoAxis = gizmoAxis;
                            _selectedObject = existingTarget.GetOriginal();
                            _currentGizmoMode = currentGizmoMode;

                            if (_selectedObject != null)
                            {
                                _objectPositionKeyFrame = FindKeyFramePairOrNull(_selectedObject.Position);
                                _objectRotationKeyFrame = FindKeyFramePairOrNull(_selectedObject.Rotation);
                                _objectScaleKeyFrame = FindKeyFramePairOrNull(_selectedObject.Scale);
                            }

                            e.Handled = true;
                            return;
                        }
                    }
                }

                // Gizmoがクリックされなかった場合、オブジェクトのヒットテストを行う
                // HitTestWithPathを使用して階層パスを取得
                var hitPath = RenderThread.Dispatcher.Invoke(() =>
                    sceneResource.Renderer.HitTestWithPath(screenPoint));

                if (hitPath.Count > 0)
                {
                    // 階層的選択: シングルクリックでルート、ダブルクリックで1階層下を選択
                    Object3D.Resource? targetResource = null;
                    bool isDoubleClick = e.ClickCount >= 2;

                    // 現在の選択がパスに含まれているか確認
                    int currentIndex = -1;
                    if (currentGizmoTarget.HasValue)
                    {
                        for (int i = 0; i < hitPath.Count; i++)
                        {
                            if (hitPath[i].GetOriginal()?.Id == currentGizmoTarget.Value)
                            {
                                currentIndex = i;
                                break;
                            }
                        }
                    }

                    if (isDoubleClick && currentIndex >= 0)
                    {
                        // ダブルクリック: 現在の選択から1階層下を選択
                        targetResource = currentIndex < hitPath.Count - 1
                            ? hitPath[currentIndex + 1] // 1階層下を選択
                            : hitPath[currentIndex]; // 最深部の場合は維持
                    }
                    else if (currentIndex >= 0)
                    {
                        // シングルクリック: 現在の選択がパスに含まれている場合は維持
                        targetResource = hitPath[currentIndex];
                    }
                    else
                    {
                        // 現在の選択がパスに含まれていない場合はルートを選択
                        targetResource = hitPath[0];
                    }

                    _selectedObject = targetResource?.GetOriginal();

                    if (_selectedObject != null)
                    {
                        // GizmoTargetを設定
                        _scene3D.GizmoTarget.CurrentValue = _selectedObject.Id;

                        // ViewModelのSelectedGizmoModeを使用
                        _currentGizmoMode = ViewModel.SelectedGizmoMode.Value;
                        _scene3D.GizmoMode.CurrentValue = _currentGizmoMode;

                        // キーフレームを探す
                        _objectPositionKeyFrame = FindKeyFramePairOrNull(_selectedObject.Position);
                        _objectRotationKeyFrame = FindKeyFramePairOrNull(_selectedObject.Rotation);
                        _objectScaleKeyFrame = FindKeyFramePairOrNull(_selectedObject.Scale);
                    }
                }
                else
                {
                    // 何もないところをクリックしたらGizmoを解除
                    _scene3D.GizmoTarget.CurrentValue = null;
                    _selectedObject = null;
                }

                e.Handled = true;
            }
            else if (pointerPoint.Properties.IsRightButtonPressed)
            {
                _rightPressed = true;

                if (_camera != null)
                {
                    // カメラの方向からYawとPitchを計算する
                    var position = _camera.Position.GetValue(RenderContext);
                    var target = _camera.Target.GetValue(RenderContext);
                    var forward = Vector3.Normalize(target - position);

                    _yaw = MathF.Atan2(forward.X, forward.Z);
                    _pitch = MathF.Asin(-forward.Y);

                    // キーフレームを探す
                    _positionKeyFrame = FindKeyFramePairOrNull(_camera.Position);
                    _targetKeyFrame = FindKeyFramePairOrNull(_camera.Target);
                }

                e.Handled = true;
            }
        }

        public void OnMoved(PointerEventArgs e)
        {
            AvaPoint position = e.GetPosition(Image);
            AvaPoint delta = position - _lastPosition;

            if (_leftPressed && _selectedObject != null && _camera != null)
            {
                // カメラの向きに基づいて移動方向を計算
                var cameraPosition = _camera.Position.GetValue(RenderContext);
                var cameraTarget = _camera.Target.GetValue(RenderContext);
                var forward = Vector3.Normalize(cameraTarget - cameraPosition);
                var up = _camera.Up.GetValue(RenderContext);
                var right = Vector3.Normalize(Vector3.Cross(forward, up));
                var cameraUp = Vector3.Normalize(Vector3.Cross(right, forward));

                switch (_currentGizmoMode)
                {
                    case GizmoMode.Translate:
                        {
                            Vector3 movement;

                            if (_selectedGizmoAxis != GizmoAxis.None)
                            {
                                // マウス移動をカメラ平面上の移動に変換
                                var screenMovement = (right * (float)delta.X + cameraUp * -(float)delta.Y) *
                                                     ObjectMoveSpeed;

                                if (_selectedGizmoAxis is GizmoAxis.X or GizmoAxis.Y or GizmoAxis.Z)
                                {
                                    // 軸拘束移動: 選択した軸に沿って移動
                                    var axisDirection = _selectedGizmoAxis switch
                                    {
                                        GizmoAxis.X => Vector3.UnitX,
                                        GizmoAxis.Y => Vector3.UnitY,
                                        GizmoAxis.Z => Vector3.UnitZ,
                                        _ => Vector3.Zero
                                    };

                                    // 軸方向に投影
                                    float projection = Vector3.Dot(screenMovement, axisDirection);
                                    movement = axisDirection * projection;
                                }
                                else
                                {
                                    // 平面拘束移動: 選択した平面上を移動
                                    var (axis1, axis2) = _selectedGizmoAxis switch
                                    {
                                        GizmoAxis.XY => (Vector3.UnitX, Vector3.UnitY),
                                        GizmoAxis.YZ => (Vector3.UnitY, Vector3.UnitZ),
                                        GizmoAxis.ZX => (Vector3.UnitZ, Vector3.UnitX),
                                        _ => (Vector3.Zero, Vector3.Zero)
                                    };

                                    // 平面に投影
                                    float proj1 = Vector3.Dot(screenMovement, axis1);
                                    float proj2 = Vector3.Dot(screenMovement, axis2);
                                    movement = axis1 * proj1 + axis2 * proj2;
                                }
                            }
                            else
                            {
                                // 自由移動: カメラ平面上を移動
                                movement = (right * (float)delta.X + cameraUp * -(float)delta.Y) * ObjectMoveSpeed;
                            }

                            if (!SetKeyFrameValue(_objectPositionKeyFrame, movement))
                            {
                                _selectedObject.Position.CurrentValue += movement;
                            }
                        }
                        break;

                    case GizmoMode.Rotate:
                        {
                            Vector3 rotation;

                            if (_selectedGizmoAxis != GizmoAxis.None)
                            {
                                // 軸拘束回転: 選択した軸周りのみ回転
                                float rotationAmount = ((float)delta.X + (float)delta.Y) * ObjectRotateSpeed;
                                rotation = _selectedGizmoAxis switch
                                {
                                    GizmoAxis.X => new Vector3(rotationAmount, 0, 0),
                                    GizmoAxis.Y => new Vector3(0, rotationAmount, 0),
                                    GizmoAxis.Z => new Vector3(0, 0, rotationAmount),
                                    _ => Vector3.Zero
                                };
                            }
                            else
                            {
                                // 自由回転: X移動→Y軸回転、Y移動→X軸回転
                                rotation = new Vector3(
                                    (float)delta.Y * ObjectRotateSpeed,
                                    (float)delta.X * ObjectRotateSpeed,
                                    0);
                            }

                            if (!SetKeyFrameValue(_objectRotationKeyFrame, rotation))
                            {
                                _selectedObject.Rotation.CurrentValue += rotation;
                            }
                        }
                        break;

                    case GizmoMode.Scale:
                        {
                            float scaleFactor = 1.0f + (float)delta.Y * ObjectScaleSpeed;
                            var currentScale = _selectedObject.Scale.CurrentValue;
                            Vector3 scaleDelta;

                            if (_selectedGizmoAxis == GizmoAxis.All)
                            {
                                // 均一スケール（中央キューブ）
                                scaleDelta = currentScale * (scaleFactor - 1.0f);
                            }
                            else if (_selectedGizmoAxis is GizmoAxis.X or GizmoAxis.Y or GizmoAxis.Z)
                            {
                                // 軸拘束スケール: 選択した軸のみスケール
                                float axisScale = scaleFactor - 1.0f;
                                scaleDelta = _selectedGizmoAxis switch
                                {
                                    GizmoAxis.X => new Vector3(currentScale.X * axisScale, 0, 0),
                                    GizmoAxis.Y => new Vector3(0, currentScale.Y * axisScale, 0),
                                    GizmoAxis.Z => new Vector3(0, 0, currentScale.Z * axisScale),
                                    _ => Vector3.Zero
                                };
                            }
                            else
                            {
                                // デフォルト: 均一スケール
                                scaleDelta = currentScale * (scaleFactor - 1.0f);
                            }

                            if (!SetKeyFrameValue(_objectScaleKeyFrame, scaleDelta))
                            {
                                _selectedObject.Scale.CurrentValue = currentScale + scaleDelta;
                            }
                        }
                        break;
                }

                _lastPosition = position;
                e.Handled = true;
            }
            else if (_rightPressed && _camera != null)
            {
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
                var cameraPosition = _camera.Position.GetValue(RenderContext);
                var newTarget = cameraPosition + forward;
                var targetDelta = newTarget - _camera.Target.GetValue(RenderContext);

                if (!SetKeyFrameValue(_targetKeyFrame, targetDelta))
                {
                    _camera.Target.CurrentValue = newTarget;
                }

                _lastPosition = position;
                e.Handled = true;
            }
        }

        public void OnReleased(PointerReleasedEventArgs e)
        {
            if (_leftPressed && e.InitialPressMouseButton == MouseButton.Left)
            {
                _leftPressed = false;

                if (_selectedObject != null)
                {
                    EditViewModel.HistoryManager.Commit(CommandNames.TransformElement);
                }

                _selectedObject = null;
                _objectPositionKeyFrame = null;
                _objectRotationKeyFrame = null;
                _objectScaleKeyFrame = null;
                _selectedGizmoAxis = GizmoAxis.None;
            }
            else if (_rightPressed && e.InitialPressMouseButton == MouseButton.Right)
            {
                _rightPressed = false;
                _positionKeyFrame = null;
                _targetKeyFrame = null;
                StopMovementTimer();
                _pressedKeys.Clear();
                EditViewModel.HistoryManager.Commit(CommandNames.TransformElement);
            }
        }

        public void OnWheelChanged(PointerWheelEventArgs e)
        {
            if (_camera != null)
            {
                // カメラとシーンを探す（ホイール操作は単独で行われる可能性があるため）
                if (_scene3D == null)
                {
                    FindScene3DAndCamera();
                }

                if (_camera == null) return;

                // キーフレームを探す
                var posKeyFrame = FindKeyFramePairOrNull(_camera.Position);
                var targetKeyFrame = FindKeyFramePairOrNull(_camera.Target);

                var position = _camera.Position.GetValue(RenderContext);
                var target = _camera.Target.GetValue(RenderContext);
                var forward = Vector3.Normalize(target - position);

                float speed = (float)e.Delta.Y * MoveSpeed * 3;
                var movement = forward * speed;

                if (!SetKeyFrameValue(posKeyFrame, movement))
                {
                    _camera.Position.CurrentValue = position + movement;
                }

                if (!SetKeyFrameValue(targetKeyFrame, movement))
                {
                    _camera.Target.CurrentValue = target + movement;
                }

                EditViewModel.HistoryManager.Commit(CommandNames.TransformElement);
                e.Handled = true;
            }
        }

        public void OnKeyDown(KeyEventArgs e)
        {
            if (!_rightPressed || _camera == null)
                return;

            _pressedKeys.Add(e.Key);
            StartMovementTimer();
            e.Handled = true;
        }

        public void OnKeyUp(KeyEventArgs e)
        {
            _pressedKeys.Remove(e.Key);

            if (_pressedKeys.Count == 0)
            {
                StopMovementTimer();
            }
        }

        private void ProcessMovement()
        {
            if (_camera == null)
                return;

            var position = _camera.Position.GetValue(RenderContext);
            var target = _camera.Target.GetValue(RenderContext);
            var forward = Vector3.Normalize(target - position);
            var up = _camera.Up.GetValue(RenderContext);
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
                if (!SetKeyFrameValue(_positionKeyFrame, movement))
                {
                    _camera.Position.CurrentValue = position + movement;
                }

                if (!SetKeyFrameValue(_targetKeyFrame, movement))
                {
                    _camera.Target.CurrentValue = target + movement;
                }
            }
        }

        private void StartMovementTimer()
        {
            if (_movementTimer != null)
                return;

            _movementTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(16) // ~60fps
            };
            _movementTimer.Tick += OnMovementTimerTick;
            _movementTimer.Start();
        }

        private void StopMovementTimer()
        {
            if (_movementTimer == null)
                return;

            _movementTimer.Stop();
            _movementTimer.Tick -= OnMovementTimerTick;
            _movementTimer = null;
        }

        private void OnMovementTimerTick(object? sender, EventArgs e)
        {
            if (_camera == null || _pressedKeys.Count == 0 || !_rightPressed)
            {
                StopMovementTimer();
                return;
            }

            ProcessMovement();
        }

        private void FindScene3DAndCamera()
        {
            _scene3D = null;
            _camera = null;

            // 選択されているオブジェクトから探す
            if (EditViewModel.SelectedObject.Value is Element element)
            {
                var op = element.Operation.Children.OfType<Scene3DOperator>().FirstOrDefault();
                if (op != null)
                {
                    _scene3D = op.Value;
                    _camera = _scene3D.Camera.CurrentValue;
                    return;
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

        private Scene3D.Resource? FindScene3DResource()
        {
            var renderer = EditViewModel.Renderer.Value;

            var layer = renderer.RenderScene[_scene3D!.ZIndex];
            var node = layer.FindRenderNode(_scene3D);
            return node == null ? null : FindScene3DRenderNode(node)?.Scene?.Resource;

            Scene3DRenderNode? FindScene3DRenderNode(RenderNode rn)
            {
                if (rn is Scene3DRenderNode sceneNode)
                {
                    return sceneNode;
                }
                else if (rn is ContainerRenderNode container)
                {
                    return container.Children
                        .Select(FindScene3DRenderNode)
                        .OfType<Scene3DRenderNode>()
                        .FirstOrDefault();
                }

                return null;
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
