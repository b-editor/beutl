using System.Numerics;
using Avalonia.Controls;
using Avalonia.Input;
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

namespace Beutl.Views;

public partial class PlayerView
{
    private sealed class KeyFrameState<T>(KeyFrame<T>? previous, KeyFrame<T>? next)
    {
        public KeyFrame<T>? Previous { get; } = previous;

        public KeyFrame<T>? Next { get; } = next;
    }

    private static KeyFrameState<T>? FindKeyFramePair<T>(
        IProperty<T> property, IEditorClock clock, Scene scene, TimeSpan? localStart)
    {
        int rate = scene.FindHierarchicalParent<Project>() is { } proj ? proj.GetFrameRate() : 30;
        TimeSpan globalKeyTime = clock.CurrentTime.Value;
        TimeSpan localKeyTime = localStart.HasValue ? globalKeyTime - localStart.Value : globalKeyTime;

        if (property.Animation is KeyFrameAnimation<T> animation)
        {
            TimeSpan keyTime = animation.UseGlobalClock ? globalKeyTime : localKeyTime;
            keyTime = keyTime.RoundToRate(rate);

            (IKeyFrame? prev, IKeyFrame? next) = animation.KeyFrames.GetPreviousAndNextKeyFrame(keyTime);

            if (next?.KeyTime == keyTime)
                return new(next as KeyFrame<T>, null);

            return new(prev as KeyFrame<T>, next as KeyFrame<T>);
        }

        return null;
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

        void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
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

    private sealed class MouseControlTransformHandles : IMouseControlHandler
    {
        // Drag lifecycle: Press → (first OnMoved → Ensure) → Move* → Release.
        // _ensured == null means Press fired but no mouse movement yet — Ensure (which mutates the
        // document) is deferred to the first OnMoved so a bare click leaves the Transform alone.
        // PressTransform anchors detection of undo/redo replacing the Transform before that first move.
        private sealed record PressState(
            Drawable Drawable,
            Element? Element,
            double FrameScale,
            BtlSize LocalSize,
            BtlMatrix StartUserMatrix,
            BtlMatrix InvStartUserMatrix,
            BtlPoint PivotLocal,
            AvaPoint PivotImage,
            AvaPoint StartImagePos,
            Transform? PressTransform);

        private sealed class EnsuredState
        {
            public required TranslateTransform Translate { get; init; }
            public required ScaleTransform Scale { get; init; }
            public required RotationTransform Rotation { get; init; }
            // Identifier used to detect undo/redo replacement via reference equality with drawable.Transform.CurrentValue.
            public required TransformGroup Group { get; init; }
            // null = non-invertible (HandleTranslate will abort).
            public BtlMatrix? InvPostMatrixOfT { get; init; }
            public required BtlMatrix RotationMatrix { get; init; }
            public required float StartTransX { get; init; }
            public required float StartTransY { get; init; }
            public required float StartScaleX { get; init; }
            public required float StartScaleY { get; init; }
            public required float StartRotation { get; init; }
            public KeyFrameState<float>? KfTransX { get; init; }
            public KeyFrameState<float>? KfTransY { get; init; }
            public KeyFrameState<float>? KfScaleX { get; init; }
            public KeyFrameState<float>? KfScaleY { get; init; }
            public KeyFrameState<float>? KfRotation { get; init; }
            public required (float prev, float next) KfStartTransX { get; init; }
            public required (float prev, float next) KfStartTransY { get; init; }
            public required (float prev, float next) KfStartScaleX { get; init; }
            public required (float prev, float next) KfStartScaleY { get; init; }
            public required (float prev, float next) KfStartRotation { get; init; }
        }

        private readonly ILogger _logger = Log.CreateLogger<MouseControlTransformHandles>();

        private PressState? _press;
        private EnsuredState? _ensured;

        // Avalonia does not auto-release pointer capture on button-up; without ResetSession releasing it
        // explicitly, framePanel would keep stealing pointer events from sibling controls after the drag.
        private IPointer? _capturedPointer;

        private bool _changed;
        private bool _shift;

        public Drawable? Drawable => _press?.Drawable;

        public required PlayerView View { get; init; }

        public required PlayerViewModel ViewModel { get; init; }

        public required IEditorClock Clock { get; init; }

        public required IEditorSelection EditorSelection { get; init; }

        public required TransformHandlesOverlay.HandleKind Kind { get; init; }

        public EditViewModel EditViewModel => ViewModel.EditViewModel;

        private Control Image => View.image;

        private KeyFrameState<float>? FindKf(IProperty<float> property)
            => FindKeyFramePair(property, Clock, EditViewModel.Scene, _press?.Element?.Start);

        private bool TryGetSession(
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out PressState? press,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out EnsuredState? ensured)
        {
            press = _press;
            ensured = _ensured;
            if (press != null && ensured != null) return true;
            _logger.LogError(
                "MouseControlTransformHandles handler invoked without session (press={HasPress}, ensured={HasEnsured}, kind={Kind}). Aborting drag.",
                press != null, ensured != null, Kind);
            ResetSession();
            return false;
        }

        private static bool ApplyDelta(KeyFrameState<float>? kf, (float prev, float next) start, float delta)
            => KeyFrameDeltaHelper.ApplyDelta(kf?.Previous, kf?.Next, start.prev, start.next, delta);

        private static (float prev, float next) CaptureStartValues(KeyFrameState<float>? kf, float fallback)
            => KeyFrameDeltaHelper.CaptureStartValues(kf?.Previous, kf?.Next, fallback);

        // Writes to surrounding keyframes when present, otherwise to CurrentValue. The two write paths
        // are intentionally mutually exclusive so a single drag does not double-write the same axis.
        private static void WriteScalar(
            IProperty<float> property,
            KeyFrameState<float>? kf,
            (float prev, float next) kfStart,
            float delta,
            float newValue)
        {
            if (!ApplyDelta(kf, kfStart, delta))
                property.CurrentValue = newValue;
        }

        public void OnPressed(PointerPressedEventArgs e)
        {
            PointerPoint pp = e.GetCurrentPoint(Image);
            if (!pp.Properties.IsLeftButtonPressed) return;

            // If Shift+click happens before framePanel has focus, no KeyDown fires, so initialize
            // _shift from the modifier state on the pointer event.
            _shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

            if (Kind == TransformHandlesOverlay.HandleKind.None)
            {
                OnPressedHitTest(e, pp);
                return;
            }

            TransformHandlesOverlay overlay = View.transformHandlesOverlay;
            Drawable? drawable = overlay.Drawable;
            Element? element = overlay.Element;
            double frameScale = overlay.FrameScale;
            BtlSize localSize = overlay.LocalSize;
            BtlMatrix startUserMatrix = overlay.UserMatrix;
            BtlPoint pivotLocal = overlay.PivotLocal;

            if (drawable == null || element == null || frameScale <= 0
                || localSize.Width <= 0 || localSize.Height <= 0
                || !startUserMatrix.TryInvert(out BtlMatrix invStartUserMatrix))
            {
                _logger.LogWarning(
                    "Transform handle press cancelled: kind={Kind}, drawable={DrawableType}, element='{Element}', frameScale={FrameScale}",
                    Kind, drawable?.GetType().Name ?? "null", element?.Name ?? "null", frameScale);
                _press = null;
                e.Handled = true;
                return;
            }

            AvaPoint startImagePos = pp.Position;
            AvaPoint pivotImage = overlay.PivotImage;

            _press = new PressState(
                Drawable: drawable,
                Element: element,
                FrameScale: frameScale,
                LocalSize: localSize,
                StartUserMatrix: startUserMatrix,
                InvStartUserMatrix: invStartUserMatrix,
                PivotLocal: pivotLocal,
                PivotImage: pivotImage,
                StartImagePos: startImagePos,
                PressTransform: drawable.Transform.CurrentValue);

            _ensured = null;

            EditorSelection.SelectedObject.Value = element;
            View.framePanel.Cursor = TransformHandlesOverlay.GetCursorForHandle(Kind);

            // Capture the pointer so handle drags receive Released even when the cursor leaves
            // framePanel. Without this, releasing outside the control would leave _press dangling
            // and subsequent re-entries would mutate the Transform without a held button.
            e.Pointer.Capture(View.framePanel);
            _capturedPointer = e.Pointer;

            e.Handled = true;
        }

        // Kind == None path: resolve the drawable via renderer hit-test instead of consuming a handle.
        private void OnPressedHitTest(PointerPressedEventArgs e, PointerPoint pp)
        {
            Scene scene = EditViewModel.Scene;
            AvaPoint imagePos = pp.Position;
            double frameScale = Image.Bounds.Size.Width / scene.FrameSize.Width;
            if (frameScale <= 0)
            {
                // Click arrived before framePanel laid out the image (layout race) — swallow it.
                _logger.LogDebug(
                    "OnPressedHitTest: frameScale={FrameScale} <= 0 (imageBounds={ImageBounds}, frameSize={FrameSize}), swallowing click.",
                    frameScale, Image.Bounds.Size, scene.FrameSize);
                _press = null;
                e.Handled = true;
                return;
            }

            AvaPoint scaledStartPosition = new(imagePos.X / frameScale, imagePos.Y / frameScale);

            Drawable? drawable;
            try
            {
                drawable = RenderThread.Dispatcher.Invoke(() =>
                {
                    var compositor = EditViewModel.Renderer.Value.Compositor;
                    var compositionFrame = compositor.EvaluateGraphics(Clock.CurrentTime.Value);
                    return EditViewModel.Renderer.Value.HitTest(compositionFrame,
                        new((float)scaledStartPosition.X, (float)scaledStartPosition.Y));
                });
            }
            catch (OperationCanceledException ocex)
            {
                // Likely shutdown — propagating would crash the UI-thread pointer pipeline.
                _logger.LogDebug(
                    ocex,
                    "OnPressedHitTest: hit-test cancelled (likely shutdown). Swallowing click.");
                _press = null;
                e.Handled = true;
                return;
            }
            catch (Exception ex) when (
                ex is not OutOfMemoryException
                and not StackOverflowException
                and not System.Runtime.InteropServices.SEHException  // propagate GPU driver crashes
                and not AccessViolationException)                    // CSE defense-in-depth
            {
                // Avoid crashing the editor via the pointer-event pipeline.
                _logger.LogError(
                    ex,
                    "OnPressedHitTest: renderer hit-test threw at scaledPos={ScaledPos}.",
                    scaledStartPosition);
                _press = null;
                e.Handled = true;
                return;
            }

            // Empty click: let normal selection logic run instead of swallowing it.
            if (drawable == null)
            {
                _press = null;
                return;
            }

            // Walk hierarchical parents so overlapping ZIndex elements don't alias to each other; fall
            // back to a ZIndex/time-window search only when the drawable's parent is outside this scene
            // (e.g. nested SceneDrawable).
            Element? element = drawable.FindHierarchicalParent<Element>();
            if (element == null || !scene.Children.Contains(element))
            {
                int zindex = drawable.ZIndex;
                TimeSpan time = Clock.CurrentTime.Value;
                element = scene.Children.FirstOrDefault(v =>
                    v.IsEnabled
                    && v.ZIndex == zindex
                    && v.Start <= time
                    && time < v.Range.End);
            }

            if (element != null)
            {
                EditorSelection.SelectedObject.Value = element;
            }

            _press = new PressState(
                Drawable: drawable,
                Element: element,
                FrameScale: frameScale,
                LocalSize: default,
                StartUserMatrix: BtlMatrix.Identity,
                InvStartUserMatrix: BtlMatrix.Identity,
                PivotLocal: default,
                PivotImage: default,
                StartImagePos: imagePos,
                PressTransform: drawable.Transform.CurrentValue);
            _ensured = null;

            // Capture the pointer so a translate drag started here still delivers Released even when
            // the cursor leaves framePanel during the drag.
            e.Pointer.Capture(View.framePanel);
            _capturedPointer = e.Pointer;

            e.Handled = true;

            // Double-click on shape → path editor
            if (e.ClickCount == 2 && drawable is Graphics.Shapes.Shape shape)
            {
                ElementPropertyTabViewModel? tab = EditViewModel.FindToolTab<ElementPropertyTabViewModel>();
                if (tab != null)
                {
                    foreach (EngineObjectPropertyViewModel item in tab.Items)
                    {
                        IPropertyEditorContext? prop = item.Properties.FirstOrDefault(v => v is GeometryEditorViewModel);
                        if (prop is GeometryEditorViewModel geometryEditorViewModel)
                        {
                            EditViewModel.Player.PathEditor.StartEdit(shape, geometryEditorViewModel, scaledStartPosition);
                            break;
                        }
                    }
                }
            }
        }

        private void EnsureOnFirstMove()
        {
            if (_ensured != null || _press == null) return;

            var ctx = new CompositionContext(Clock.CurrentTime.Value);

            // Snapshot before Ensure mutates the document — a non-finite value here aborts the drag so
            // we don't leave a structural mutation that the user can't fix via undo. Fallbacks must match
            // the property defaults of the R/S/T inserted by Ensure (T=0, S=100, R=0).
            var (probeR, probeS, probeT) = CanonicalTransformLayout.FindCanonicalTransforms(_press.Drawable.Transform.GetValue(ctx));
            float startTransX = probeT?.X.GetValue(ctx) ?? 0f;
            float startTransY = probeT?.Y.GetValue(ctx) ?? 0f;
            float startScaleX = probeS?.ScaleX.GetValue(ctx) ?? 100f;
            float startScaleY = probeS?.ScaleY.GetValue(ctx) ?? 100f;
            float startRotation = probeR?.Rotation.GetValue(ctx) ?? 0f;

            if (!float.IsFinite(startTransX) || !float.IsFinite(startTransY)
                || !float.IsFinite(startScaleX) || !float.IsFinite(startScaleY)
                || !float.IsFinite(startRotation))
            {
                AbortDrag(
                    "EnsureOnFirstMove: non-finite snapshot (T=({Tx},{Ty}), S=({Sx},{Sy}), R={Rot}); aborting drag before structural mutation.",
                    startTransX, startTransY, startScaleX, startScaleY, startRotation);
                return;
            }

            CanonicalTransformLayoutResult ensured =
                CanonicalTransformLayout.Ensure(_press.Drawable, ctx);

            KeyFrameState<float>? kfTransX = FindKf(ensured.Translate.X);
            KeyFrameState<float>? kfTransY = FindKf(ensured.Translate.Y);
            KeyFrameState<float>? kfScaleX = FindKf(ensured.Scale.ScaleX);
            KeyFrameState<float>? kfScaleY = FindKf(ensured.Scale.ScaleY);
            KeyFrameState<float>? kfRotation = FindKf(ensured.Rotation.Rotation);

            BtlMatrix? invPostMatrixOfT = ensured.PostMatrixOfT.TryInvert(out BtlMatrix invPostT) ? invPostT : null;
            BtlMatrix rotationMatrix = ensured.Rotation.CreateMatrix(ctx);

            _ensured = new EnsuredState
            {
                Translate = ensured.Translate,
                Scale = ensured.Scale,
                Rotation = ensured.Rotation,
                Group = ensured.Group,
                InvPostMatrixOfT = invPostMatrixOfT,
                RotationMatrix = rotationMatrix,
                StartTransX = startTransX,
                StartTransY = startTransY,
                StartScaleX = startScaleX,
                StartScaleY = startScaleY,
                StartRotation = startRotation,
                KfTransX = kfTransX,
                KfTransY = kfTransY,
                KfScaleX = kfScaleX,
                KfScaleY = kfScaleY,
                KfRotation = kfRotation,
                KfStartTransX = CaptureStartValues(kfTransX, startTransX),
                KfStartTransY = CaptureStartValues(kfTransY, startTransY),
                KfStartScaleX = CaptureStartValues(kfScaleX, startScaleX),
                KfStartScaleY = CaptureStartValues(kfScaleY, startScaleY),
                KfStartRotation = CaptureStartValues(kfRotation, startRotation),
            };

            // Commit even on a zero-delta drag so the structural change is undoable on its own.
            if (ensured.StructureChanged)
            {
                _changed = true;
            }
        }

        public void OnMoved(PointerEventArgs e)
        {
            if (_press == null) return;

            // If undo/redo replaced the Transform between OnPressed and the first OnMoved, run Ensure
            // against the wrong instance — abort the drag instead. After Ensure, PressTransform may
            // legitimately differ, so the post-Ensure guard below watches _ensured.Group instead.
            if (_ensured == null
                && !ReferenceEquals(_press.Drawable.Transform.CurrentValue, _press.PressTransform))
            {
                AbortDrag(
                    "OnMoved: drawable Transform replaced before first move (Drawable={DrawableType}, Kind={Kind}, oldGroup={OldGroupType}, newGroup={NewGroupType}); aborting drag.",
                    _press.Drawable.GetType().Name,
                    Kind,
                    _press.PressTransform?.GetType().Name ?? "null",
                    _press.Drawable.Transform.CurrentValue?.GetType().Name ?? "null");
                return;
            }

            EnsureOnFirstMove();
            if (_ensured == null) return;

            // If undo/redo replaces the TransformGroup mid-drag, _ensured.Group is now detached and
            // writes would be silently dropped. Undoing a child keyframe value keeps the group ref,
            // so this only catches whole-Transform replacements.
            if (!ReferenceEquals(_press.Drawable.Transform.CurrentValue, _ensured.Group))
            {
                AbortDrag(
                    "OnMoved: drawable Transform replaced mid-drag (Drawable={DrawableType}, Kind={Kind}, ensuredGroup={EnsuredGroupType}, current={CurrentType}); aborting drag.",
                    _press.Drawable.GetType().Name,
                    Kind,
                    _ensured.Group.GetType().Name,
                    _press.Drawable.Transform.CurrentValue?.GetType().Name ?? "null");
                return;
            }

            PointerPoint pp = e.GetCurrentPoint(Image);
            AvaPoint currentImg = pp.Position;

            // KeyDown/KeyUp only fire while framePanel has focus, so losing focus causes OnKeyUp to be
            // missed and _shift to get stuck. During a drag, treat the pointer-event KeyModifiers as
            // authoritative.
            _shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

            switch (Kind)
            {
                case TransformHandlesOverlay.HandleKind.None:
                case TransformHandlesOverlay.HandleKind.Center:
                    HandleTranslate(currentImg);
                    break;
                case TransformHandlesOverlay.HandleKind.TopLeft:
                case TransformHandlesOverlay.HandleKind.TopRight:
                case TransformHandlesOverlay.HandleKind.BottomRight:
                case TransformHandlesOverlay.HandleKind.BottomLeft:
                    HandleCorner(currentImg);
                    break;
                case TransformHandlesOverlay.HandleKind.Top:
                case TransformHandlesOverlay.HandleKind.Bottom:
                case TransformHandlesOverlay.HandleKind.Left:
                case TransformHandlesOverlay.HandleKind.Right:
                    HandleEdge(currentImg);
                    break;
                case TransformHandlesOverlay.HandleKind.Rotate:
                    HandleRotate(currentImg);
                    break;
                default:
                    throw new System.ArgumentOutOfRangeException(nameof(Kind), Kind, "Unhandled HandleKind in OnMoved switch.");
            }

            // AbortDrag in any branch nulls _press; in that case the pointer event should bubble up
            // to the parent routing rather than be marked as handled.
            if (_press == null) return;

            _changed = true;
            InvalidateFrameCache();
            e.Handled = true;
        }

        private void HandleTranslate(AvaPoint currentImg)
        {
            if (!TryGetSession(out PressState? press, out EnsuredState? ensured)) return;

            double scale = press.FrameScale;
            AvaPoint scaledCurrent = new(currentImg.X / scale, currentImg.Y / scale);
            AvaPoint scaledStart = new(press.StartImagePos.X / scale, press.StartImagePos.Y / scale);

            if (ensured.InvPostMatrixOfT is not BtlMatrix inv)
            {
                AbortDrag(
                    "HandleTranslate: PostMatrixOfT non-invertible (Drawable={DrawableType}); aborting drag.",
                    press.Drawable.GetType().Name);
                return;
            }

            BtlPoint pCurrent = inv.Transform(new BtlPoint((float)scaledCurrent.X, (float)scaledCurrent.Y));
            BtlPoint pStart = inv.Transform(new BtlPoint((float)scaledStart.X, (float)scaledStart.Y));
            float dx = pCurrent.X - pStart.X;
            float dy = pCurrent.Y - pStart.Y;

            float newX = ensured.StartTransX + dx;
            float newY = ensured.StartTransY + dy;
            if (!float.IsFinite(newX) || !float.IsFinite(newY))
            {
                AbortDrag(
                    "HandleTranslate: non-finite translate (newX={NewX}, newY={NewY}); aborting drag.",
                    newX, newY);
                return;
            }
            WriteScalar(ensured.Translate.X, ensured.KfTransX, ensured.KfStartTransX, dx, newX);
            WriteScalar(ensured.Translate.Y, ensured.KfTransY, ensured.KfStartTransY, dy, newY);
        }

        private void HandleCorner(AvaPoint currentImg)
        {
            if (!TryGetSession(out PressState? press, out EnsuredState? ensured)) return;

            BtlPoint currentLocal = ImagePointToStartLocal(press, currentImg);
            (double anchorX, double anchorY) = CornerAnchorLocal(press, Kind);

            // Crossing the diagonal flips ratio sign and so flips the object — accepted by design.
            bool grabLeft = Kind is TransformHandlesOverlay.HandleKind.TopLeft or TransformHandlesOverlay.HandleKind.BottomLeft;
            bool grabTop = Kind is TransformHandlesOverlay.HandleKind.TopLeft or TransformHandlesOverlay.HandleKind.TopRight;

            double newWidth = grabLeft ? (anchorX - currentLocal.X) : (currentLocal.X - anchorX);
            double newHeight = grabTop ? (anchorY - currentLocal.Y) : (currentLocal.Y - anchorY);

            double ratioX = newWidth / press.LocalSize.Width;
            double ratioY = newHeight / press.LocalSize.Height;

            if (_shift)
            {
                (ratioX, ratioY) = TransformHandleMath.LockAspect(ratioX, ratioY);
            }

            float newScaleX = (float)(ensured.StartScaleX * ratioX);
            float newScaleY = (float)(ensured.StartScaleY * ratioY);
            ApplyScaleWithPivotCorrection(press, ensured, newScaleX, newScaleY, anchorX, anchorY);
        }

        private void HandleEdge(AvaPoint currentImg)
        {
            if (!TryGetSession(out PressState? press, out EnsuredState? ensured)) return;

            BtlPoint currentLocal = ImagePointToStartLocal(press, currentImg);
            (double anchorX, double anchorY) = EdgeAnchorLocal(press, Kind);

            bool horizontal = Kind is TransformHandlesOverlay.HandleKind.Left or TransformHandlesOverlay.HandleKind.Right;
            bool grabLeft = Kind is TransformHandlesOverlay.HandleKind.Left;
            bool grabTop = Kind is TransformHandlesOverlay.HandleKind.Top;

            float newScaleX = ensured.StartScaleX;
            float newScaleY = ensured.StartScaleY;

            if (horizontal)
            {
                double newWidth = grabLeft ? (anchorX - currentLocal.X) : (currentLocal.X - anchorX);
                double ratioX = newWidth / press.LocalSize.Width;
                newScaleX = (float)(ensured.StartScaleX * ratioX);
                if (_shift)
                {
                    newScaleY = (float)(ensured.StartScaleY * ratioX);
                }
            }
            else
            {
                double newHeight = grabTop ? (anchorY - currentLocal.Y) : (currentLocal.Y - anchorY);
                double ratioY = newHeight / press.LocalSize.Height;
                newScaleY = (float)(ensured.StartScaleY * ratioY);
                if (_shift)
                {
                    newScaleX = (float)(ensured.StartScaleX * ratioY);
                }
            }

            ApplyScaleWithPivotCorrection(press, ensured, newScaleX, newScaleY, anchorX, anchorY);
        }

        private void AbortDrag(string reasonTemplate, params object?[] args)
        {
            _logger.LogWarning(reasonTemplate, args);
            ResetSession();
            View.framePanel.Cursor = null;
        }

        private void HandleRotate(AvaPoint currentImg)
        {
            if (!TryGetSession(out PressState? press, out EnsuredState? ensured)) return;
            double sx = press.StartImagePos.X - press.PivotImage.X;
            double sy = press.StartImagePos.Y - press.PivotImage.Y;
            double cx = currentImg.X - press.PivotImage.X;
            double cy = currentImg.Y - press.PivotImage.Y;

            double angleStart = Math.Atan2(sy, sx);
            double angleCurrent = Math.Atan2(cy, cx);
            double deltaRad = TransformHandleMath.NormalizeAngleDelta(angleCurrent - angleStart);
            if (!double.IsFinite(deltaRad))
            {
                AbortDrag(
                    "HandleRotate: non-finite delta (pivot={Pivot}, start={Start}, current={Current}); aborting drag.",
                    press.PivotImage, press.StartImagePos, currentImg);
                return;
            }
            float deltaDeg = MathUtilities.Rad2Deg((float)deltaRad);

            float newRot = ensured.StartRotation + deltaDeg;
            if (_shift)
            {
                newRot = (float)(Math.Round(newRot / 15.0) * 15.0);
            }

            // Derive delta from the post-Shift-snap value (deltaDeg is the pre-snap value).
            float effectiveDelta = newRot - ensured.StartRotation;
            WriteScalar(ensured.Rotation.Rotation, ensured.KfRotation, ensured.KfStartRotation, effectiveDelta, newRot);
        }

        private void ApplyScale(EnsuredState ensured, float newScaleX, float newScaleY)
        {
            if (!float.IsFinite(newScaleX) || !float.IsFinite(newScaleY))
            {
                AbortDrag(
                    "ApplyScale: non-finite scale (sx={ScaleX}, sy={ScaleY}); aborting drag.",
                    newScaleX, newScaleY);
                return;
            }
            float deltaScaleX = newScaleX - ensured.StartScaleX;
            float deltaScaleY = newScaleY - ensured.StartScaleY;
            WriteScalar(ensured.Scale.ScaleX, ensured.KfScaleX, ensured.KfStartScaleX, deltaScaleX, newScaleX);
            WriteScalar(ensured.Scale.ScaleY, ensured.KfScaleY, ensured.KfStartScaleY, deltaScaleY, newScaleY);
        }

        // Compensate the anchor shift caused by a scale change via the operative Translate; see
        // <see cref="TransformHandleMath.ComputePivotTranslationDelta"/> for the derivation.
        private void ApplyScaleWithPivotCorrection(
            PressState press, EnsuredState ensured,
            float newScaleX, float newScaleY, double anchorX, double anchorY)
        {
            ApplyScale(ensured, newScaleX, newScaleY);
            if (_press == null) return;

            (float deltaTx, float deltaTy) = TransformHandleMath.ComputePivotTranslationDelta(
                ensured.StartScaleX, ensured.StartScaleY,
                newScaleX, newScaleY,
                anchorX, anchorY,
                press.PivotLocal.X, press.PivotLocal.Y,
                ensured.RotationMatrix);
            float newTx = ensured.StartTransX + deltaTx;
            float newTy = ensured.StartTransY + deltaTy;

            if (!float.IsFinite(newTx) || !float.IsFinite(newTy))
            {
                AbortDrag(
                    "ApplyScaleWithPivotCorrection: non-finite ΔT (deltaTx={Dx}, deltaTy={Dy}); aborting drag.",
                    deltaTx, deltaTy);
                return;
            }

            WriteScalar(ensured.Translate.X, ensured.KfTransX, ensured.KfStartTransX, deltaTx, newTx);
            WriteScalar(ensured.Translate.Y, ensured.KfTransY, ensured.KfStartTransY, deltaTy, newTy);
        }

        private static BtlPoint ImagePointToStartLocal(PressState press, AvaPoint img)
        {
            double sceneX = img.X / press.FrameScale;
            double sceneY = img.Y / press.FrameScale;
            return press.InvStartUserMatrix.Transform(new BtlPoint((float)sceneX, (float)sceneY));
        }

        // Anchors are local-rect coordinates (0,0)-(w,h). Drawable.GetTransformMatrix assumes the same
        // origin; for Shapes whose Geometry.Bounds.Position != (0,0) the overlay can misalign — that is
        // a rendering-model limitation, out of scope here. Each anchor is the OPPOSITE corner/edge of
        // the grabbed handle (so the grabbed side moves while the anchor stays put).
        private static (double X, double Y) CornerAnchorLocal(PressState press, TransformHandlesOverlay.HandleKind kind)
        {
            BtlSize size = press.LocalSize;
            double w = size.Width, h = size.Height;
            return kind switch
            {
                TransformHandlesOverlay.HandleKind.TopLeft => (w, h),
                TransformHandlesOverlay.HandleKind.TopRight => (0, h),
                TransformHandlesOverlay.HandleKind.BottomRight => (0, 0),
                TransformHandlesOverlay.HandleKind.BottomLeft => (w, 0),
                _ => throw new System.ArgumentOutOfRangeException(nameof(kind), kind, "Corner anchor requested for non-corner HandleKind."),
            };
        }

        // Edge anchors are at the OPPOSITE edge's center, not a corner — using a corner would make
        // Shift-dragging an edge introduce sideways drift on the orthogonal axis.
        private static (double X, double Y) EdgeAnchorLocal(PressState press, TransformHandlesOverlay.HandleKind kind)
        {
            BtlSize size = press.LocalSize;
            double w = size.Width, h = size.Height;
            return kind switch
            {
                TransformHandlesOverlay.HandleKind.Top => (w * 0.5, h),
                TransformHandlesOverlay.HandleKind.Bottom => (w * 0.5, 0),
                TransformHandlesOverlay.HandleKind.Left => (w, h * 0.5),
                TransformHandlesOverlay.HandleKind.Right => (0, h * 0.5),
                _ => throw new System.ArgumentOutOfRangeException(nameof(kind), kind, "Edge anchor requested for non-edge HandleKind."),
            };
        }

        public void OnReleased(PointerReleasedEventArgs e)
        {
            if (_press == null) return;

            // If undo/redo replaced the Transform mid-drag (after the post-Ensure guard already ran),
            // _ensured.Group is detached — discard the pending commit. Click-only sessions skip this
            // since Ensure never ran.
            if (_ensured != null
                && !ReferenceEquals(_press.Drawable.Transform.CurrentValue, _ensured.Group))
            {
                _logger.LogWarning(
                    "OnReleased: drawable Transform replaced before release (Drawable={DrawableType}, Kind={Kind}); discarding pending commit.",
                    _press.Drawable.GetType().Name, Kind);
                View.framePanel.Cursor = null;
                ResetSession();
                _shift = false;
                e.Handled = true;
                return;
            }

            if (_changed)
            {
                EditViewModel.HistoryManager.Commit(CommandNames.TransformElement);
            }

            View.framePanel.Cursor = null;
            ResetSession();
            _shift = false;
            e.Handled = true;
        }

        // Rollback is a no-op after Commit (Commit clears the current transaction), but on abort/discard
        // paths it must run so pending Ensure-driven structural changes and handle deltas don't leak into
        // an unrelated next Commit.
        private void ResetSession()
        {
            if (_changed || _ensured != null)
            {
                EditViewModel.HistoryManager.Rollback();
            }
            if (_capturedPointer != null)
            {
                // PointerCaptureLost may have already released for us; guard against double-clearing.
                if (ReferenceEquals(_capturedPointer.Captured, View.framePanel))
                {
                    _capturedPointer.Capture(null);
                }
                _capturedPointer = null;
            }
            _press = null;
            _ensured = null;
            _changed = false;
        }

        public void OnKeyDown(KeyEventArgs e) => SyncShift(e.KeyModifiers);

        public void OnKeyUp(KeyEventArgs e) => SyncShift(e.KeyModifiers);

        public void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
        {
            // OnReleased clears _press on the normal path, so this only fires when capture was taken
            // away externally (focus stolen, system event, etc.).
            if (_press == null) return;
            _logger.LogDebug(
                "OnPointerCaptureLost: capture lost mid-drag (Drawable={DrawableType}, Kind={Kind}); discarding pending changes.",
                _press.Drawable.GetType().Name, Kind);
            View.framePanel.Cursor = null;
            ResetSession();
            _shift = false;
        }

        private void SyncShift(KeyModifiers modifiers) => _shift = modifiers.HasFlag(KeyModifiers.Shift);

        private void InvalidateFrameCache()
        {
            Element? element = _press?.Element;
            if (element == null) return;
            int rate = EditViewModel.Player.GetFrameRate();
            int st = (int)element.Start.ToFrameNumber(rate);
            int ed = (int)Math.Ceiling(element.Range.End.ToFrameNumber(rate));
            EditViewModel.FrameCacheManager.Value.DeleteAndUpdateBlocks([(st, ed)]);
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

        public void OnReleased(PointerReleasedEventArgs e)
        {
            if (_pressed)
            {
                float scale = ViewModel.Scene!.FrameSize.Width / (float)Image.Bounds.Width;
                Rect rect = new Rect(_start.ToBtlPoint() * scale, _position.ToBtlPoint() * scale).Normalize();

                if (ViewModel.TcsForCrop == null)
                {
                    var copyAsString = new FAMenuFlyoutItem()
                    {
                        Text = Strings.Copy,
                        IconSource = new FASymbolIconSource() { Symbol = FASymbol.Copy }
                    };
                    var saveAsImage = new FAMenuFlyoutItem()
                    {
                        Text = Strings.SaveAsImage,
                        IconSource = new FASymbolIconSource() { Symbol = FASymbol.SaveAs }
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
                            IconSource = new FASymbolIconSource() { Symbol = FASymbol.ImageCopy }
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
                    var position = _camera.Position.GetValue(CompositionContext);
                    var target = _camera.Target.GetValue(CompositionContext);
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
                var cameraPosition = _camera.Position.GetValue(CompositionContext);
                var cameraTarget = _camera.Target.GetValue(CompositionContext);
                var forward = Vector3.Normalize(cameraTarget - cameraPosition);
                var up = _camera.Up.GetValue(CompositionContext);
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
                var cameraPosition = _camera.Position.GetValue(CompositionContext);
                var newTarget = cameraPosition + forward;
                var targetDelta = newTarget - _camera.Target.GetValue(CompositionContext);

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
                var compositor = EditViewModel.Renderer.Value.Compositor;
                var compositionFrame = compositor.EvaluateGraphics(Clock.CurrentTime.Value);
                return EditViewModel.Renderer.Value.HitTest(compositionFrame, new((float)scaledPos.X, (float)scaledPos.Y));
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

    private void OnFramePointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        // OnFramePointerReleased nulls _mouseState on the normal path; this only fires when capture
        // was taken away externally before Release reached us.
        if (_mouseState == null) return;
        _mouseState.OnPointerCaptureLost(e);
        _mouseState = null;
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
            // Move-mode pointer-down is handled directly in OnFramePointerPressed; this path only
            // serves wheel events, where a no-op handler is fine.
            return new MouseControlTransformHandles
            {
                View = this,
                ViewModel = viewModel,
                Clock = viewModel.EditViewModel.GetRequiredService<IEditorClock>(),
                EditorSelection = viewModel.EditViewModel.GetRequiredService<IEditorSelection>(),
                Kind = TransformHandlesOverlay.HandleKind.None,
            };
        }
        else if (viewModel.IsHandMode.Value)
        {
            return new MouseControlHand { ViewModel = viewModel, View = this };
        }
        else if (viewModel.IsCameraMode.Value)
        {
            return new MouseControl3DCamera
            {
                ViewModel = viewModel,
                Clock = viewModel.EditViewModel.GetRequiredService<IEditorClock>(),
                EditorSelection = viewModel.EditViewModel.GetRequiredService<IEditorSelection>(),
                View = this
            };
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

                // In Move mode, left button funnels through MouseControlTransformHandles whether or not
                // it hit a handle (Kind == None takes the hit-test/double-click/translate-drag path).
                if (viewModel.IsMoveMode.Value && point.Properties.IsLeftButtonPressed)
                {
                    AvaPoint imagePoint = e.GetCurrentPoint(image).Position;
                    TransformHandlesOverlay.HandleKind kind = transformHandlesOverlay.HitTest(imagePoint);
                    var handler = new MouseControlTransformHandles
                    {
                        View = this,
                        ViewModel = viewModel,
                        Clock = viewModel.EditViewModel.GetRequiredService<IEditorClock>(),
                        EditorSelection = viewModel.EditViewModel.GetRequiredService<IEditorSelection>(),
                        Kind = kind,
                    };
                    _mouseState = handler;
                    handler.OnPressed(e);
                    _lastSelected.SetTarget(handler.Drawable);
                    framePanel.Focus();
                    return;
                }

                _mouseState = CreateMouseHandler(viewModel);
                _mouseState.OnPressed(e);
            }
        }
    }
}
