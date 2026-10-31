using Avalonia.Input;
using Beutl.Animation;
using Beutl.Editor.Components.Helpers;
using Beutl.Engine;
using Beutl.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using BtlPoint = Beutl.Graphics.Point;

namespace Beutl.Views;

public partial class PlayerView
{
    private sealed class MouseControlEditingHandle(PlayerView view, PlayerViewModel model,
        IEditingHandleProvider provider, EditingHandle handle) : IEditingInteraction
    {
        private IPointer? _pointer;
        private Beutl.Graphics.Matrix _inverse;
        private BtlPoint _start;
        private PlayerViewportTransform? _viewport;
        private MediaTime _time;
        private DragSession? _drag;
        private KeyFrameState<BtlPoint>? _keyFrames;
        private (BtlPoint Previous, BtlPoint Next) _keyFrameStart;
        private BtlPoint _valueStart;
        private BtlPoint _appliedDelta;

        private BtlPoint Local(PointerEventArgs e)
        {
            Avalonia.Point point = _viewport!.ToFrame(e.GetPosition(view.image));
            return _inverse.Transform(new BtlPoint((float)point.X, (float)point.Y));
        }

        public void OnPressed(PointerPressedEventArgs e)
        {
            if (model.IsPlaybackActive || !e.GetCurrentPoint(view.image).Properties.IsLeftButtonPressed
                || view.transformHandlesOverlay.Viewport is not { } viewport
                || !view.transformHandlesOverlay.UserMatrix.TryInvert(out _inverse)) return;
            _viewport = viewport;
            IEditorClock clock = model.EditViewModel.GetRequiredService<IEditorClock>();
            _time = clock.CurrentTime.Value;
            _start = Local(e);
            _appliedDelta = default;
            if (handle.Property is { } property)
            {
                _keyFrames = FindKeyFramePair(property, clock, model.EditViewModel.Scene);
                _valueStart = property.Value;
                _keyFrameStart = KeyFrameDeltaHelper.CaptureStartValues(_keyFrames?.Previous, _keyFrames?.Next, _valueStart);
            }
            _pointer = e.Pointer;
            _pointer.Capture(view.framePanel);
            e.Handled = true;
        }

        public void OnMoved(PointerEventArgs e)
        {
            if (_pointer is null) return;
            if (model.IsPlaybackActive)
            {
                EndInteraction();
                return;
            }
            BtlPoint current = Local(e);
            var position = new BtlPoint(handle.Position.X + current.X - _start.X, handle.Position.Y + current.Y - _start.Y);
            if (position == handle.Position && _drag is null) return;
            _drag ??= DragSession.Begin(model.EditViewModel, view.framePanel, CommandNames.TransformElement,
                Reset, Reset);
            if (_drag is null)
            {
                Reset();
                return;
            }
            using (_drag.Enter())
                Move(position);
            e.Handled = true;
        }

        // A bound property moves like a transform handle's value: by the drag delta, on its surrounding
        // keyframes when it is animated.
        private void Move(BtlPoint position)
        {
            if (handle.Property is not { } property)
            {
                provider.MoveEditingHandle(handle.Id, position, _time);
                return;
            }

            BtlPoint delta = position - handle.Position;
            if (_keyFrames?.Components is { } components)
            {
                BtlPoint offset = delta - _appliedDelta;
                components.TransformValues(value => value + offset, _time);
                _appliedDelta = delta;
            }
            else if (!KeyFrameDeltaHelper.ApplyDelta(_keyFrames?.Previous, _keyFrames?.Next,
                    _keyFrameStart.Previous, _keyFrameStart.Next, delta))
                property.Value = _valueStart + delta;
        }

        public void OnReleased(PointerReleasedEventArgs e)
        {
            EndInteraction();
            e.Handled = true;
        }

        public void OnPointerCaptureLost(PointerCaptureLostEventArgs e) => EndInteraction();

        public void EndInteraction()
        {
            if (_drag is { } drag) drag.Confirm();
            else Reset();
        }

        private void Reset()
        {
            IPointer? pointer = _pointer;
            _pointer = null;
            _drag = null;
            if (pointer is not null && ReferenceEquals(pointer.Captured, view.framePanel))
                pointer.Capture(null);
        }
    }
}
