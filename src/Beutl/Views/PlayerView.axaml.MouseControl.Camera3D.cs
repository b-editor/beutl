using System.Numerics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Beutl.Animation;
using Beutl.Composition;
using Beutl.Controls;
using Beutl.Editor.Components.ElementPropertyTab.ViewModels;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.Views;
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
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Utilities;
using Beutl.ViewModels;
using Beutl.ViewModels.Editors;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using AvaImage = Avalonia.Controls.Image;
using AvaPoint = Avalonia.Point;
using AvaRect = Avalonia.Rect;
using BtlMatrix = Beutl.Graphics.Matrix;
using BtlPoint = Beutl.Graphics.Point;
using BtlRect = Beutl.Graphics.Rect;
using BtlSize = Beutl.Graphics.Size;
using FluentIconSource = FluentIcons.Avalonia.Fluent.FluentIconSource;
using Icon = FluentIcons.Common.Icon;

namespace Beutl.Views;

public partial class PlayerView
{
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
        // World units are pixels: 10 px per movement tick, 30 px per wheel notch.
        private const float MoveSpeed = 10f;
        private const float ObjectRotateSpeed = 0.5f;
        private const float ObjectScaleSpeed = 0.01f;

        public required PlayerView View { get; init; }

        public required PlayerViewModel ViewModel { get; init; }

        public required IEditorClock Clock { get; init; }

        public required IEditorSelection EditorSelection { get; init; }

        public EditViewModel EditViewModel => ViewModel.EditViewModel;

        private CompositionContext CompositionContext => field ??= new(Clock.CurrentTime.Value);

        private Control Image => View.image;

        private KeyFrameState<Vector3>? FindKeyFramePairOrNull(IProperty<Vector3> property)
        {
            int rate = EditViewModel.Scene.FindHierarchicalParent<Project>() is { } proj ? proj.GetFrameRate() : 30;
            TimeSpan globalKeyTime = Clock.CurrentTime.Value;
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
                OnLeftPressed(e);
            }
            else if (pointerPoint.Properties.IsRightButtonPressed)
            {
                OnRightPressed(e);
            }
        }

        private void OnLeftPressed(PointerPressedEventArgs e)
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
                    return FindObjectResource(objects, currentGizmoTarget.Value);
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
                            CaptureObjectKeyFrames(_selectedObject);
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
                bool isDoubleClick = e.ClickCount >= 2;
                Object3D.Resource? targetResource = SelectFromHitPath(hitPath, currentGizmoTarget, isDoubleClick);

                _selectedObject = targetResource?.GetOriginal();

                if (_selectedObject != null)
                {
                    // GizmoTargetを設定
                    _scene3D.GizmoTarget.CurrentValue = _selectedObject.Id;

                    // ViewModelのSelectedGizmoModeを使用
                    _currentGizmoMode = ViewModel.SelectedGizmoMode.Value;
                    _scene3D.GizmoMode.CurrentValue = _currentGizmoMode;

                    // キーフレームを探す
                    CaptureObjectKeyFrames(_selectedObject);
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

        private static Object3D.Resource? SelectFromHitPath(
            IReadOnlyList<Object3D.Resource> hitPath, Guid? currentGizmoTarget, bool isDoubleClick)
        {
            Object3D.Resource? targetResource = null;

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

            return targetResource;
        }

        private void CaptureObjectKeyFrames(Object3D selectedObject)
        {
            _objectPositionKeyFrame = FindKeyFramePairOrNull(selectedObject.Position);
            _objectRotationKeyFrame = FindKeyFramePairOrNull(selectedObject.Rotation);
            _objectScaleKeyFrame = FindKeyFramePairOrNull(selectedObject.Scale);
        }

        private void OnRightPressed(PointerPressedEventArgs e)
        {
            _rightPressed = true;

            if (_camera != null)
            {
                // カメラの方向からYawとPitchを計算する（+Yが下向きなので、Pitchが増えると下を向く）
                var position = _camera.Position.GetValue(CompositionContext);
                var target = _camera.Target.GetValue(CompositionContext);
                var forward = Vector3.Normalize(target - position);

                _yaw = MathF.Atan2(forward.X, forward.Z);
                _pitch = MathF.Asin(Math.Clamp(forward.Y, -1f, 1f));

                // キーフレームを探す
                _positionKeyFrame = FindKeyFramePairOrNull(_camera.Position);
                _targetKeyFrame = FindKeyFramePairOrNull(_camera.Target);
            }

            e.Handled = true;
        }

        public void OnMoved(PointerEventArgs e)
        {
            AvaPoint position = e.GetPosition(Image);
            AvaPoint delta = position - _lastPosition;

            if (_leftPressed && _selectedObject != null && _camera != null)
            {
                // カメラの向きに基づいて移動方向を計算
                var cameraPosition = _camera.Position.GetValue(CompositionContext);
                var cameraTarget = _camera.Target.GetValue(CompositionContext);
                var forward = Vector3.Normalize(cameraTarget - cameraPosition);
                var up = _camera.Up.GetValue(CompositionContext);
                var right = Vector3.Normalize(Vector3.Cross(forward, up));
                var cameraUp = Vector3.Normalize(Vector3.Cross(right, forward));

                switch (_currentGizmoMode)
                {
                    case GizmoMode.Translate:
                        TranslateSelectedObject(_selectedObject, delta, right, cameraUp);
                        break;

                    case GizmoMode.Rotate:
                        RotateSelectedObject(_selectedObject, delta);
                        break;

                    case GizmoMode.Scale:
                        ScaleSelectedObject(_selectedObject, delta);
                        break;
                }

                _lastPosition = position;
                e.Handled = true;
            }
            else if (_rightPressed && _camera != null)
            {
                OrbitCamera(_camera, delta);

                _lastPosition = position;
                e.Handled = true;
            }
        }

        private void TranslateSelectedObject(Object3D selectedObject, AvaPoint delta, Vector3 right, Vector3 cameraUp)
        {
            Vector3 movement;

            if (_selectedGizmoAxis != GizmoAxis.None)
            {
                // マウス移動をカメラ平面上の移動に変換
                var screenMovement = (right * (float)delta.X + cameraUp * -(float)delta.Y) *
                                     GetWorldUnitsPerViewPixel(selectedObject);

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
                movement = (right * (float)delta.X + cameraUp * -(float)delta.Y)
                           * GetWorldUnitsPerViewPixel(selectedObject);
            }

            // The movement is in world space; Position is in the parent group's space.
            movement = ToParentSpace(selectedObject, movement);
            if (!SetKeyFrameValue(_objectPositionKeyFrame, movement))
            {
                selectedObject.Position.CurrentValue += movement;
            }
        }

        private void RotateSelectedObject(Object3D selectedObject, AvaPoint delta)
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
                selectedObject.Rotation.CurrentValue += rotation;
            }
        }

        private void ScaleSelectedObject(Object3D selectedObject, AvaPoint delta)
        {
            float scaleFactor = 1.0f + (float)delta.Y * ObjectScaleSpeed;
            var currentScale = selectedObject.Scale.CurrentValue;
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
                selectedObject.Scale.CurrentValue = currentScale + scaleDelta;
            }
        }

        private void OrbitCamera(Camera3D camera, AvaPoint delta)
        {
            // マウスの動きに応じてYawとPitchを更新（ドラッグした方向へシーンを掴んで回す）
            _yaw -= (float)delta.X * RotationSpeed;
            _pitch += (float)delta.Y * RotationSpeed;

            _pitch = Math.Clamp(_pitch, (-MathF.PI / 2) + 0.1f, (MathF.PI / 2) - 0.1f);

            // 新しいforward directionを計算する
            var forward = new Vector3(
                MathF.Sin(_yaw) * MathF.Cos(_pitch),
                MathF.Sin(_pitch),
                MathF.Cos(_yaw) * MathF.Cos(_pitch)
            );

            // カメラのターゲットを、注視点までの距離を保ったまま更新する
            var cameraPosition = camera.Position.GetValue(CompositionContext);
            var currentTarget = camera.Target.GetValue(CompositionContext);
            float targetDistance = MathF.Max(Vector3.Distance(cameraPosition, currentTarget), 1f);
            var newTarget = cameraPosition + forward * targetDistance;
            var targetDelta = newTarget - currentTarget;

            if (!SetKeyFrameValue(_targetKeyFrame, targetDelta))
            {
                camera.Target.CurrentValue = newTarget;
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

                var position = _camera.Position.GetValue(CompositionContext);
                var target = _camera.Target.GetValue(CompositionContext);
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

            var position = _camera.Position.GetValue(CompositionContext);
            var target = _camera.Target.GetValue(CompositionContext);
            var forward = Vector3.Normalize(target - position);
            var up = _camera.Up.GetValue(CompositionContext);
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

            _movementTimer = new DispatcherTimer(DispatcherPriority.Background, View.Dispatcher)
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

        // The world distance one pixel of the preview covers at the object's depth, so a drag moves the
        // object with the pointer.
        private float GetWorldUnitsPerViewPixel(Object3D obj)
        {
            if (_camera == null || _scene3D == null)
                return 1f;

            double viewHeight = Image.Bounds.Height;
            if (viewHeight <= 0)
                return 1f;

            Scene scene = EditViewModel.Scene;
            float sceneHeight = scene.FrameSize.Height;
            float renderHeight = _scene3D.RenderHeight.GetValue(CompositionContext);
            float renderWidth = _scene3D.RenderWidth.GetValue(CompositionContext);
            // The 3D render is drawn at its own size into the scene, which the preview then fits to the view.
            double renderPixelsPerViewPixel = sceneHeight / viewHeight;
            float worldPerRenderPixel = _camera switch
            {
                PerspectiveCamera perspective => 2
                    * GizmoHitTester.GetViewDepth(
                        _camera.Position.GetValue(CompositionContext),
                        _camera.Target.GetValue(CompositionContext),
                        _camera.NearPlane.GetValue(CompositionContext),
                        GetWorldPosition(obj))
                    * MathF.Tan(perspective.FieldOfView.GetValue(CompositionContext) * MathF.PI / 360f)
                    / renderHeight,
                OrthographicCamera orthographic => orthographic.Width.GetValue(CompositionContext) / renderWidth,
                _ => 1f,
            };

            return (float)(worldPerRenderPixel * renderPixelsPerViewPixel);
        }

        // Where the object is drawn, including the groups it is nested in; its Position alone is local.
        private Vector3 GetWorldPosition(Object3D obj)
        {
            Scene3D.Resource? sceneResource = _scene3D != null ? FindScene3DResource() : null;
            Vector3? position = sceneResource == null
                ? null
                : RenderThread.Dispatcher.Invoke(() =>
                {
                    Object3D.Resource? target = FindObjectResource(sceneResource.Objects, obj.Id);
                    return target == null
                        ? (Vector3?)null
                        : Renderer3D.GetWorldPosition(sceneResource.Objects, target);
                });

            return position ?? obj.Position.GetValue(CompositionContext);
        }

        // Converts a world-space direction into the space of the groups the object is nested in.
        private Vector3 ToParentSpace(Object3D obj, Vector3 worldDelta)
        {
            Scene3D.Resource? sceneResource = _scene3D != null ? FindScene3DResource() : null;
            if (sceneResource == null)
                return worldDelta;

            Matrix4x4 parent = RenderThread.Dispatcher.Invoke(() =>
                FindObjectResource(sceneResource.Objects, obj.Id) is { } target
                    ? Renderer3D.GetParentWorldMatrix(sceneResource.Objects, target)
                    : Matrix4x4.Identity);

            return Matrix4x4.Invert(parent, out Matrix4x4 inverse)
                ? Vector3.TransformNormal(worldDelta, inverse)
                : worldDelta;
        }

        // Searches nested objects too: a hit test can select an object inside a group. Disabled objects are
        // not drawn, so they are not found.
        private static Object3D.Resource? FindObjectResource(IReadOnlyList<Object3D.Resource> objects, Guid id)
        {
            foreach (Object3D.Resource item in objects)
            {
                if (!item.IsEnabled)
                    continue;

                if (item.GetOriginal()?.Id == id)
                    return item;

                if (FindObjectResource(item.GetChildResources(), id) is { } child)
                    return child;
            }

            return null;
        }

        private void FindScene3DAndCamera()
        {
            _scene3D = null;
            _camera = null;

            // 選択されているオブジェクトから探す
            if (EditorSelection.SelectedObject.Value is Element element)
            {
                var scene3DObj = element.Objects.OfType<Scene3D>().FirstOrDefault();
                if (scene3DObj != null)
                {
                    _scene3D = scene3DObj;
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
            {
                var renderer = EditViewModel.Renderer.Value;
                var compositionFrame = renderer.Compositor.EvaluateGraphics(Clock.CurrentTime.Value);
                var point = new Point((float)scaledPos.X, (float)scaledPos.Y);
                Drawable? hit = renderer.HitTest(compositionFrame, point);
                if (hit is Scene3D)
                    return hit;

                // A scene with a transparent background lets clicks on its empty areas through, but the camera
                // is still controlled from there: fall back to the topmost scene whose area holds the point,
                // among those drawn over whatever the hit test found.
                for (int i = compositionFrame.Objects.Length - 1; i >= 0; i--)
                {
                    EngineObject? original = compositionFrame.Objects[i].GetOriginal();
                    if (hit != null && ReferenceEquals(original, hit))
                        break;

                    if (original is Scene3D candidate
                        && renderer.GetBoundary(candidate) is { } bounds
                        && bounds.Contains(point))
                    {
                        return candidate;
                    }
                }

                return hit;
            });

            if (drawable is Scene3D scene3D)
            {
                _scene3D = scene3D;
                _camera = scene3D.Camera.CurrentValue;
            }
        }

        private Scene3D.Resource? FindScene3DResource()
        {
            var renderer = EditViewModel.Renderer.Value;

            var node = renderer.FindRenderNode(_scene3D!);
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
}
