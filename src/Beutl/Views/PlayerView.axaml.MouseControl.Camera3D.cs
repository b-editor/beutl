using System.Numerics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Beutl.Composition;
using Beutl.Controls;
using Beutl.Editor.Components.Helpers;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics3D;
using Beutl.Graphics3D.Camera;
using Beutl.Graphics3D.Gizmo;
using Beutl.ProjectSystem;
using Beutl.ViewModels;
using Microsoft.Extensions.Logging;
using AvaPoint = Avalonia.Point;

namespace Beutl.Views;

public partial class PlayerView
{
    private sealed class MouseControl3DCamera : IEditingInteraction
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
        // Frame to render pixels for the drag in progress, found at its first move.
        private Matrix? _frameToScene;

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
            return FindKeyFramePair(property, Clock, EditViewModel.Scene);
        }

        // キーフレームがない場合はfalseを返す
        private static bool SetKeyFrameValue(KeyFrameState<Vector3>? keyframes, Vector3 delta)
        {
            if (keyframes?.Components is { } components)
            {
                components.TransformValues(value => value + delta, keyframes.Time);
                return true;
            }
            switch ((keyframes?.Previous, keyframes?.Next))
            {
                case (null, null):
                    return false;

                case ({ } prev, { } next):
                    prev.Value.Value += delta;
                    next.Value.Value += delta;
                    break;

                case ({ } prev, null):
                    prev.Value.Value += delta;
                    break;

                case (null, { } next):
                    next.Value.Value += delta;
                    break;
            }

            return true;
        }

        public void OnPressed(PointerPressedEventArgs e)
        {
            if (StopForPlayback())
            {
                e.Handled = true;
                return;
            }

            PointerPoint pointerPoint = e.GetCurrentPoint(Image);
            _lastPosition = pointerPoint.Position;

            FindScene3DAndCamera();

            if (pointerPoint.Properties.IsLeftButtonPressed)
            {
                try
                {
                    OnLeftPressed(e);
                }
                catch (TimeoutException ex)
                {
                    // An answer the render thread did not give must not select or deselect anything.
                    s_logger.LogWarning(ex, "Skipped a 3D press: the render thread did not answer in time.");
                    _leftPressed = false;
                    e.Handled = true;
                }
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
            _frameToScene = null;

            if (_scene3D == null || View.ViewportTransform is not { } viewport)
                return;

            var sceneResource = RenderThread.Dispatcher.Invoke(() => FindScene3DResource(), RenderThreadWait);
            if (sceneResource?.Renderer == null)
                return;

            // Renderer3D hit-tests in RenderWidth x RenderHeight space, which the scene's 2D transform places in the frame.
            Scene3D scene3D = _scene3D;
            Matrix frameToScene = RenderThread.Dispatcher.Invoke(() => GetFrameToScene(scene3D), RenderThreadWait);
            _frameToScene = frameToScene;
            Point screenPoint = frameToScene.Transform(viewport.ToFrame(_lastPosition).ToBtlPoint());

            var currentGizmoTarget = _scene3D.GizmoTarget.Value;
            var currentGizmoMode = _scene3D.GizmoMode.Value;

            if (currentGizmoTarget.HasValue && currentGizmoMode != GizmoMode.None)
            {
                var existingTarget = RenderThread.Dispatcher.Invoke(() =>
                {
                    var objects = sceneResource.Objects.Where(o => o.IsEnabled).ToList();
                    return FindObjectResource(objects, currentGizmoTarget.Value);
                }, RenderThreadWait);

                if (existingTarget != null)
                {
                    var gizmoAxis = RenderThread.Dispatcher.Invoke(() =>
                        sceneResource.Renderer.GizmoHitTest(screenPoint, existingTarget, currentGizmoMode), RenderThreadWait);

                    if (gizmoAxis != GizmoAxis.None)
                    {
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

            var hitPath = RenderThread.Dispatcher.Invoke(() =>
                sceneResource.Renderer.HitTestWithPath(screenPoint), RenderThreadWait);

            if (hitPath.Count > 0)
            {
                // 階層的選択: シングルクリックでルート、ダブルクリックで1階層下を選択
                bool isDoubleClick = e.ClickCount >= 2;
                Object3D.Resource? targetResource = SelectFromHitPath(hitPath, currentGizmoTarget, isDoubleClick);

                _selectedObject = targetResource?.GetOriginal();

                if (_selectedObject != null)
                {
                    _scene3D.GizmoTarget.Value = _selectedObject.Id.Value;

                    _currentGizmoMode = ViewModel.SelectedGizmoMode.Value;
                    _scene3D.GizmoMode.Value = _currentGizmoMode;

                    CaptureObjectKeyFrames(_selectedObject);
                }
            }
            else
            {
                _scene3D.GizmoTarget.Value = null;
                _selectedObject = null;
            }

            e.Handled = true;
        }

        private static Object3D.Resource? SelectFromHitPath(
            IReadOnlyList<Object3D.Resource> hitPath, Guid? currentGizmoTarget, bool isDoubleClick)
        {
            Object3D.Resource? targetResource = null;

            int currentIndex = -1;
            if (currentGizmoTarget.HasValue)
            {
                for (int i = 0; i < hitPath.Count; i++)
                {
                    if (hitPath[i].GetOriginal()?.Id.Value == currentGizmoTarget.Value)
                    {
                        currentIndex = i;
                        break;
                    }
                }
            }

            if (isDoubleClick && currentIndex >= 0)
            {
                targetResource = currentIndex < hitPath.Count - 1
                    ? hitPath[currentIndex + 1]
                    : hitPath[currentIndex];
            }
            else if (currentIndex >= 0)
            {
                targetResource = hitPath[currentIndex];
            }
            else
            {
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
                var position = _camera.Position.Evaluate(CompositionContext);
                var target = _camera.Target.Evaluate(CompositionContext);
                var forward = Vector3.Normalize(target - position);

                _yaw = MathF.Atan2(forward.X, forward.Z);
                _pitch = MathF.Asin(Math.Clamp(forward.Y, -1f, 1f));

                _positionKeyFrame = FindKeyFramePairOrNull(_camera.Position);
                _targetKeyFrame = FindKeyFramePairOrNull(_camera.Target);
            }

            e.Handled = true;
        }

        public void OnMoved(PointerEventArgs e)
        {
            if (StopForPlayback()) return;

            AvaPoint position = e.GetPosition(Image);
            AvaPoint delta = position - _lastPosition;

            if (_leftPressed && _selectedObject != null && _camera != null)
            {
                var cameraPosition = _camera.Position.Evaluate(CompositionContext);
                var cameraTarget = _camera.Target.Evaluate(CompositionContext);
                var forward = Vector3.Normalize(cameraTarget - cameraPosition);
                var up = _camera.Up.Evaluate(CompositionContext);
                var right = Vector3.Normalize(Vector3.Cross(forward, up));
                var cameraUp = Vector3.Normalize(Vector3.Cross(right, forward));

                switch (_currentGizmoMode)
                {
                    case GizmoMode.Translate:
                        try
                        {
                            TranslateSelectedObject(_selectedObject, ToRenderDelta(_lastPosition, position), right, cameraUp);
                        }
                        catch (TimeoutException ex)
                        {
                            // Without the render thread's world coordinates the move would go the wrong way.
                            s_logger.LogWarning(ex, "Skipped a 3D move: the render thread did not answer in time.");
                        }

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

        private void TranslateSelectedObject(Object3D selectedObject, AvaPoint renderDelta, Vector3 right, Vector3 cameraUp)
        {
            Vector3 movement;

            if (_selectedGizmoAxis != GizmoAxis.None)
            {
                var screenMovement = (right * (float)renderDelta.X + cameraUp * -(float)renderDelta.Y) *
                                     GetWorldUnitsPerRenderPixel(selectedObject);

                if (_selectedGizmoAxis is GizmoAxis.X or GizmoAxis.Y or GizmoAxis.Z)
                {
                    var axisDirection = _selectedGizmoAxis switch
                    {
                        GizmoAxis.X => Vector3.UnitX,
                        GizmoAxis.Y => Vector3.UnitY,
                        GizmoAxis.Z => Vector3.UnitZ,
                        _ => Vector3.Zero
                    };

                    float projection = Vector3.Dot(screenMovement, axisDirection);
                    movement = axisDirection * projection;
                }
                else
                {
                    var (axis1, axis2) = _selectedGizmoAxis switch
                    {
                        GizmoAxis.XY => (Vector3.UnitX, Vector3.UnitY),
                        GizmoAxis.YZ => (Vector3.UnitY, Vector3.UnitZ),
                        GizmoAxis.ZX => (Vector3.UnitZ, Vector3.UnitX),
                        _ => (Vector3.Zero, Vector3.Zero)
                    };

                    float proj1 = Vector3.Dot(screenMovement, axis1);
                    float proj2 = Vector3.Dot(screenMovement, axis2);
                    movement = axis1 * proj1 + axis2 * proj2;
                }
            }
            else
            {
                movement = (right * (float)renderDelta.X + cameraUp * -(float)renderDelta.Y)
                           * GetWorldUnitsPerRenderPixel(selectedObject);
            }

            // The movement is in world space; Position is in the parent group's space.
            movement = ToParentSpace(selectedObject, movement);
            if (!SetKeyFrameValue(_objectPositionKeyFrame, movement))
            {
                selectedObject.Position.Value += movement;
            }
        }

        private void RotateSelectedObject(Object3D selectedObject, AvaPoint delta)
        {
            Vector3 rotation;

            if (_selectedGizmoAxis != GizmoAxis.None)
            {
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
                selectedObject.Rotation.Value += rotation;
            }
        }

        private void ScaleSelectedObject(Object3D selectedObject, AvaPoint delta)
        {
            float scaleFactor = 1.0f + (float)delta.Y * ObjectScaleSpeed;
            var currentScale = selectedObject.Scale.Value;
            Vector3 scaleDelta;

            if (_selectedGizmoAxis is GizmoAxis.X or GizmoAxis.Y or GizmoAxis.Z)
            {
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
                scaleDelta = currentScale * (scaleFactor - 1.0f);
            }

            if (!SetKeyFrameValue(_objectScaleKeyFrame, scaleDelta))
            {
                selectedObject.Scale.Value = currentScale + scaleDelta;
            }
        }

        private void OrbitCamera(Camera3D camera, AvaPoint delta)
        {
            // マウスの動きに応じてYawとPitchを更新（ドラッグした方向へシーンを掴んで回す）
            _yaw -= (float)delta.X * RotationSpeed;
            _pitch += (float)delta.Y * RotationSpeed;

            _pitch = Math.Clamp(_pitch, (-MathF.PI / 2) + 0.1f, (MathF.PI / 2) - 0.1f);

            var forward = new Vector3(
                MathF.Sin(_yaw) * MathF.Cos(_pitch),
                MathF.Sin(_pitch),
                MathF.Cos(_yaw) * MathF.Cos(_pitch)
            );

            // カメラのターゲットを、注視点までの距離を保ったまま更新する
            var cameraPosition = camera.Position.Evaluate(CompositionContext);
            var currentTarget = camera.Target.Evaluate(CompositionContext);
            float targetDistance = MathF.Max(Vector3.Distance(cameraPosition, currentTarget), 1f);
            var newTarget = cameraPosition + forward * targetDistance;
            var targetDelta = newTarget - currentTarget;

            if (!SetKeyFrameValue(_targetKeyFrame, targetDelta))
            {
                camera.Target.Value = newTarget;
            }
        }

        public void OnReleased(PointerReleasedEventArgs e)
        {
            if ((_leftPressed && e.InitialPressMouseButton == MouseButton.Left)
                || (_rightPressed && e.InitialPressMouseButton == MouseButton.Right))
            {
                EndInteraction();
            }
        }

        public void EndInteraction()
        {
            bool commit = _rightPressed || (_leftPressed && _selectedObject != null);
            _leftPressed = false;
            _rightPressed = false;
            _selectedObject = null;
            _objectPositionKeyFrame = null;
            _objectRotationKeyFrame = null;
            _objectScaleKeyFrame = null;
            _selectedGizmoAxis = GizmoAxis.None;
            _frameToScene = null;
            _positionKeyFrame = null;
            _targetKeyFrame = null;
            StopMovementTimer();
            _pressedKeys.Clear();
            if (commit) EditViewModel.HistoryManager.Commit(CommandNames.TransformElement);
        }

        // Input and timer callbacks can run before the UI-side IsPlaying subscription is delivered.
        private bool StopForPlayback()
        {
            if (!ViewModel.IsPlaybackActive) return false;
            EndInteraction();
            return true;
        }

        public void OnWheelChanged(PointerWheelEventArgs e)
        {
            if (StopForPlayback()) return;

            // Each wheel step gets a new handler, so find the camera under the pointer first.
            if (_camera == null)
            {
                _lastPosition = e.GetPosition(Image);
                FindScene3DAndCamera();
                if (_camera == null) return;
            }

            var posKeyFrame = FindKeyFramePairOrNull(_camera.Position);
            var targetKeyFrame = FindKeyFramePairOrNull(_camera.Target);

            var position = _camera.Position.Evaluate(CompositionContext);
            var target = _camera.Target.Evaluate(CompositionContext);
            // A camera on its target has no direction to move along.
            if (target == position) return;
            var forward = Vector3.Normalize(target - position);

            float speed = (float)e.Delta.Y * MoveSpeed * 3;
            var movement = forward * speed;

            if (!SetKeyFrameValue(posKeyFrame, movement))
            {
                _camera.Position.Value = position + movement;
            }

            if (!SetKeyFrameValue(targetKeyFrame, movement))
            {
                _camera.Target.Value = target + movement;
            }

            EditViewModel.HistoryManager.Commit(CommandNames.TransformElement);
            e.Handled = true;
        }

        public void OnKeyDown(KeyEventArgs e)
        {
            if (StopForPlayback()) return;

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

            var position = _camera.Position.Evaluate(CompositionContext);
            var target = _camera.Target.Evaluate(CompositionContext);
            var forward = Vector3.Normalize(target - position);
            var up = _camera.Up.Evaluate(CompositionContext);
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
                    _camera.Position.Value = position + movement;
                }

                if (!SetKeyFrameValue(_targetKeyFrame, movement))
                {
                    _camera.Target.Value = target + movement;
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
            if (StopForPlayback()) return;

            if (_camera == null || _pressedKeys.Count == 0 || !_rightPressed)
            {
                StopMovementTimer();
                return;
            }

            ProcessMovement();
        }

        // The pointer's move in the scene's render pixels, through the preview's fit and the scene's 2D transform.
        private AvaPoint ToRenderDelta(AvaPoint from, AvaPoint to)
        {
            if (View.ViewportTransform is not { } viewport || _scene3D is not { } scene3D)
                return default;

            Matrix frameToScene = _frameToScene ??= RenderThread.Dispatcher.Invoke(() => GetFrameToScene(scene3D), RenderThreadWait);
            Point start = frameToScene.Transform(viewport.ToFrame(from).ToBtlPoint());
            Point end = frameToScene.Transform(viewport.ToFrame(to).ToBtlPoint());
            return new AvaPoint(end.X - start.X, end.Y - start.Y);
        }

        // The inverse of the matrix the scene is drawn with; identity when the renderer has none for it.
        private Matrix GetFrameToScene(Scene3D scene3D)
        {
            SceneRenderer renderer = EditViewModel.Renderer.Value;
            renderer.UpdateFrame(renderer.Compositor.EvaluateGraphics(Clock.CurrentTime.Value));
            return renderer.GetLocalBoundary(scene3D) is { } drawn && drawn.Transform.TryInvert(out Matrix inverse)
                ? inverse
                : Matrix.Identity;
        }

        // The world distance one render pixel covers at the object's depth.
        private float GetWorldUnitsPerRenderPixel(Object3D obj)
        {
            if (_camera == null || _scene3D == null)
                return 1f;

            float renderHeight = _scene3D.RenderHeight.Evaluate(CompositionContext);
            float renderWidth = _scene3D.RenderWidth.Evaluate(CompositionContext);
            return _camera switch
            {
                PerspectiveCamera perspective => 2
                    * GizmoHitTester.GetViewDepth(
                        _camera.Position.Evaluate(CompositionContext),
                        _camera.Target.Evaluate(CompositionContext),
                        _camera.NearPlane.Evaluate(CompositionContext),
                        GetWorldPosition(obj))
                    * MathF.Tan(perspective.FieldOfView.Evaluate(CompositionContext) * MathF.PI / 360f)
                    / renderHeight,
                OrthographicCamera orthographic => orthographic.Width.Evaluate(CompositionContext) / renderWidth,
                _ => 1f,
            };
        }

        // Where the object is drawn, including the groups it is nested in; its Position alone is local.
        private Vector3 GetWorldPosition(Object3D obj)
        {
            Vector3? position = _scene3D == null
                ? null
                : RenderThread.Dispatcher.Invoke(() =>
                {
                    if (FindScene3DResource() is not { } sceneResource
                        || FindObjectResource(sceneResource.Objects, obj.Id.Value) is not { } target)
                    {
                        return (Vector3?)null;
                    }

                    return Renderer3D.GetWorldPosition(sceneResource.Objects, target);
                }, RenderThreadWait);

            return position ?? obj.Position.Evaluate(CompositionContext);
        }

        // Converts a world-space direction into the space of the groups the object is nested in.
        private Vector3 ToParentSpace(Object3D obj, Vector3 worldDelta)
        {
            if (_scene3D == null)
                return worldDelta;

            Matrix4x4 parent = RenderThread.Dispatcher.Invoke(() =>
                FindScene3DResource() is { } sceneResource && FindObjectResource(sceneResource.Objects, obj.Id.Value) is { } target
                    ? Renderer3D.GetParentWorldMatrix(sceneResource.Objects, target)
                    : Matrix4x4.Identity, RenderThreadWait);

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

                if (item.GetOriginal()?.Id.Value == id)
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

            if (EditorSelection.SelectedObject.Value is Element element)
            {
                var scene3DObj = element.Objects.OfType<Scene3D>().FirstOrDefault();
                if (scene3DObj != null)
                {
                    _scene3D = scene3DObj;
                    _camera = _scene3D.Camera.Value;
                    return;
                }
            }

            if (View.ViewportTransform is not { } viewport) return;
            Point point = viewport.ToFrame(_lastPosition).ToBtlPoint();

            var drawable = QueryRenderThread<Drawable?>(() =>
            {
                var renderer = EditViewModel.Renderer.Value;
                var compositionFrame = renderer.Compositor.EvaluateGraphics(Clock.CurrentTime.Value);
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
            }, null);

            if (drawable is Scene3D scene3D)
            {
                _scene3D = scene3D;
                _camera = scene3D.Camera.Value;
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
