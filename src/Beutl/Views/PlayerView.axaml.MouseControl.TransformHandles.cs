using Avalonia.Controls;
using Avalonia.Input;
using Beutl.Animation;
using Beutl.Composition;
using Beutl.Editor.Components.ElementPropertyTab.ViewModels;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Transformation;
using Beutl.Logging;
using Beutl.ProjectSystem;
using Beutl.Utilities;
using Beutl.ViewModels;
using Beutl.ViewModels.Editors;
using Microsoft.Extensions.Logging;
using AvaPoint = Avalonia.Point;
using BtlMatrix = Beutl.Graphics.Matrix;
using BtlPoint = Beutl.Graphics.Point;
using BtlRect = Beutl.Graphics.Rect;

namespace Beutl.Views;

public partial class PlayerView
{
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
            BtlRect LocalBounds,
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
            // Takes the group's output into the frame: the pivot's offset back, the alignment and anything above
            // the drawable. null when the press-time matrix cannot be split there (a singular group).
            public BtlMatrix? AfterGroup { get; init; }
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
            if (StopForPlayback())
            {
                e.Handled = true;
                return;
            }

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
            BtlRect localBounds = overlay.LocalBounds;
            BtlMatrix startUserMatrix = overlay.UserMatrix;
            BtlPoint pivotLocal = overlay.PivotLocal;

            if (drawable == null || element == null || frameScale <= 0
                || localBounds.Width <= 0 || localBounds.Height <= 0
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
                LocalBounds: localBounds,
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
                element = FindEnabledElementAt(scene, zindex, time);
            }

            if (element != null)
            {
                EditorSelection.SelectedObject.Value = element;
            }

            _press = new PressState(
                Drawable: drawable,
                Element: element,
                FrameScale: frameScale,
                LocalBounds: default,
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

            BtlMatrix rotationMatrix = ensured.Rotation.CreateMatrix(ctx);
            // The press-time box was drawn through (-pivot) · group · AfterGroup.
            BtlMatrix intoGroup = BtlMatrix.CreateTranslation(-_press.PivotLocal.X, -_press.PivotLocal.Y)
                * ensured.Group.CreateMatrix(ctx);
            BtlMatrix? afterGroup = intoGroup.TryInvert(out BtlMatrix outOfGroup)
                ? outOfGroup * _press.StartUserMatrix
                : null;
            // A translate moves the drawable through everything applied after it, a transition's zoom included.
            BtlMatrix afterTranslate = afterGroup is { } groupToFrame
                && !(ensured.PostMatrixOfT * groupToFrame).ContainsPerspective()
                    ? ensured.PostMatrixOfT * groupToFrame
                    : ensured.PostMatrixOfT;
            BtlMatrix? invPostMatrixOfT = afterTranslate.TryInvert(out BtlMatrix invPostT) ? invPostT : null;

            _ensured = new EnsuredState
            {
                Translate = ensured.Translate,
                Scale = ensured.Scale,
                Rotation = ensured.Rotation,
                Group = ensured.Group,
                InvPostMatrixOfT = invPostMatrixOfT,
                AfterGroup = afterGroup,
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
            if (StopForPlayback()) return;

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

            double ratioX = newWidth / press.LocalBounds.Width;
            double ratioY = newHeight / press.LocalBounds.Height;

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
                double ratioX = newWidth / press.LocalBounds.Width;
                newScaleX = (float)(ensured.StartScaleX * ratioX);
                if (_shift)
                {
                    newScaleY = (float)(ensured.StartScaleY * ratioX);
                }
            }
            else
            {
                double newHeight = grabTop ? (anchorY - currentLocal.Y) : (currentLocal.Y - anchorY);
                double ratioY = newHeight / press.LocalBounds.Height;
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

        // Compensate the anchor shift caused by a scale change via the operative Translate. The translate is solved
        // from the group as the new scale leaves it, so the anchor stays put whatever order the group's transforms
        // are in and whatever uniform Scale they carry; a group that cannot be solved falls back to the canonical
        // [T, R, S] formula in <see cref="TransformHandleMath.ComputePivotTranslationDelta"/>.
        private void ApplyScaleWithPivotCorrection(
            PressState press, EnsuredState ensured,
            float newScaleX, float newScaleY, double anchorX, double anchorY)
        {
            ApplyScale(ensured, newScaleX, newScaleY);
            if (_press == null) return;

            (float deltaTx, float deltaTy) = SolveAnchorTranslation(press, ensured, anchorX, anchorY)
                ?? TransformHandleMath.ComputePivotTranslationDelta(
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

        // The operative Translate's change from its start value that draws the anchor where the press-time box did,
        // or null when the matrices do not allow solving it. A drawn point moves by delta · L when the translate
        // moves by delta, L being the linear part of everything applied after the translate.
        private (float dx, float dy)? SolveAnchorTranslation(
            PressState press, EnsuredState ensured, double anchorX, double anchorY)
        {
            if (ensured.AfterGroup is not { } afterGroup) return null;

            var ctx = new CompositionContext(Clock.CurrentTime.Value);
            BtlMatrix toFrame = BtlMatrix.CreateTranslation(-press.PivotLocal.X, -press.PivotLocal.Y)
                * ensured.Group.CreateMatrix(ctx) * afterGroup;
            BtlMatrix afterTranslate = MatrixAfter(ensured.Group, ensured.Translate, ctx) * afterGroup;
            float det = (afterTranslate.M11 * afterTranslate.M22) - (afterTranslate.M12 * afterTranslate.M21);
            if (toFrame.ContainsPerspective() || afterTranslate.ContainsPerspective()
                || !float.IsFinite(det) || MathF.Abs(det) < 1e-6f)
            {
                return null;
            }

            var anchor = new BtlPoint((float)anchorX, (float)anchorY);
            BtlPoint target = press.StartUserMatrix.Transform(anchor);
            BtlPoint drawn = toFrame.Transform(anchor);
            float ex = target.X - drawn.X;
            float ey = target.Y - drawn.Y;
            float dx = ((ex * afterTranslate.M22) - (ey * afterTranslate.M21)) / det;
            float dy = ((ey * afterTranslate.M11) - (ex * afterTranslate.M12)) / det;

            return (ensured.Translate.X.GetValue(ctx) + dx - ensured.StartTransX,
                ensured.Translate.Y.GetValue(ctx) + dy - ensured.StartTransY);
        }

        // The enabled transforms the group applies after the given one, which are the ones before it in the list.
        private static BtlMatrix MatrixAfter(TransformGroup group, Transform transform, CompositionContext ctx)
        {
            BtlMatrix after = BtlMatrix.Identity;
            foreach (Transform child in group.Children)
            {
                if (ReferenceEquals(child, transform)) break;
                if (child.IsEnabled) after = child.CreateMatrix(ctx) * after;
            }

            return after;
        }

        private static BtlPoint ImagePointToStartLocal(PressState press, AvaPoint img)
        {
            double sceneX = img.X / press.FrameScale;
            double sceneY = img.Y / press.FrameScale;
            return press.InvStartUserMatrix.Transform(new BtlPoint((float)sceneX, (float)sceneY));
        }

        // Anchors are points on the overlay's box, in the drawable's own space (the space PivotLocal is in).
        // The box need not start at (0,0): it follows what the drawable draws, such as a drop shadow or a
        // geometry whose bounds start elsewhere. Each anchor is the OPPOSITE corner/edge of the grabbed
        // handle (so the grabbed side moves while the anchor stays put).
        private static (double X, double Y) CornerAnchorLocal(PressState press, TransformHandlesOverlay.HandleKind kind)
        {
            BtlRect bounds = press.LocalBounds;
            return kind switch
            {
                TransformHandlesOverlay.HandleKind.TopLeft => (bounds.Right, bounds.Bottom),
                TransformHandlesOverlay.HandleKind.TopRight => (bounds.Left, bounds.Bottom),
                TransformHandlesOverlay.HandleKind.BottomRight => (bounds.Left, bounds.Top),
                TransformHandlesOverlay.HandleKind.BottomLeft => (bounds.Right, bounds.Top),
                _ => throw new System.ArgumentOutOfRangeException(nameof(kind), kind, "Corner anchor requested for non-corner HandleKind."),
            };
        }

        // Edge anchors are at the OPPOSITE edge's center, not a corner — using a corner would make
        // Shift-dragging an edge introduce sideways drift on the orthogonal axis.
        private static (double X, double Y) EdgeAnchorLocal(PressState press, TransformHandlesOverlay.HandleKind kind)
        {
            BtlRect bounds = press.LocalBounds;
            BtlPoint center = bounds.Center;
            return kind switch
            {
                TransformHandlesOverlay.HandleKind.Top => (center.X, bounds.Bottom),
                TransformHandlesOverlay.HandleKind.Bottom => (center.X, bounds.Top),
                TransformHandlesOverlay.HandleKind.Left => (bounds.Right, center.Y),
                TransformHandlesOverlay.HandleKind.Right => (bounds.Left, center.Y),
                _ => throw new System.ArgumentOutOfRangeException(nameof(kind), kind, "Edge anchor requested for non-edge HandleKind."),
            };
        }

        public void OnReleased(PointerReleasedEventArgs e)
        {
            if (_press == null) return;
            EndInteraction();
            e.Handled = true;
        }

        public void EndInteraction()
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
                return;
            }

            if (_changed)
            {
                EditViewModel.HistoryManager.Commit(CommandNames.TransformElement);
            }

            View.framePanel.Cursor = null;
            ResetSession();
            _shift = false;
        }

        private bool StopForPlayback()
        {
            if (!ViewModel.IsPlaybackActive) return false;
            EndInteraction();
            return true;
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
}
