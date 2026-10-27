using Avalonia.Input;
using Beutl.Editor.Components.Helpers;
using Beutl.Engine;
using Beutl.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using BtlPoint = Beutl.Graphics.Point;

namespace Beutl.Views;

public partial class PlayerView
{
    private sealed class MouseControlEditingHandle(PlayerView view, PlayerViewModel model,
        IEditingHandleProvider provider, EditingHandle handle) : IMouseControlHandler
    {
        private IPointer? _pointer;
        private Beutl.Graphics.Matrix _inverse;
        private BtlPoint _start;
        private PlayerViewportTransform? _viewport;
        private MediaTime _time;
        private DragSession? _drag;

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
            _time = model.EditViewModel.GetRequiredService<IEditorClock>().CurrentTime.Value;
            _start = Local(e);
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
                provider.MoveEditingHandle(handle.Id, position, _time);
            e.Handled = true;
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
