using Avalonia.Input;
using Beutl.Animation;
using Beutl.Controls;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.ProjectSystem;
using Beutl.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using AvaImage = Avalonia.Controls.Image;
using AvaPoint = Avalonia.Point;
using BtlRect = Beutl.Graphics.Rect;

namespace Beutl.Views;

public partial class PlayerView
{
    private sealed class KeyFrameState<T>(KeyFrame<T>? previous, KeyFrame<T>? next)
    {
        public IComponentAnimation<T>? Components { get; init; }
        public MediaTime Time { get; init; }
        public KeyFrame<T>? Previous { get; } = previous;

        public KeyFrame<T>? Next { get; } = next;
    }

    private static KeyFrameState<T>? FindKeyFramePair<T>(
        IProperty<T> property, IEditorClock clock, Scene scene)
    {
        Rational rate = scene.FindHierarchicalParent<Project>().GetFrameRate();
        MediaTime globalKeyTime = clock.CurrentTime.Value;

        if (property.GetPropertyAnimation() is IComponentAnimation<T> components)
            return new(null, null) { Components = components, Time = globalKeyTime };

        if (property.GetKeyFrameAnimation() is KeyFrameAnimation<T> animation)
        {
            MediaTime keyTime = animation.ToLocal(globalKeyTime);
            keyTime = keyTime.RoundToFrame(rate, MidpointRounding.AwayFromZero);

            if (AnimationOperations.FindKeyFrameOnFrame(animation.KeyFrames, keyTime, rate) is KeyFrame<T> atFrame)
                return new(atFrame, null);

            (IKeyFrame? prev, IKeyFrame? next) = animation.KeyFrames.GetPreviousAndNextKeyFrame(keyTime);

            return new(prev as KeyFrame<T>, next as KeyFrame<T>);
        }

        return null;
    }

    private interface IMouseControlHandler
    {
        void OnMoved(PointerEventArgs e);

        void OnPressed(PointerPressedEventArgs e);

        void OnReleased(PointerReleasedEventArgs e);

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

    private interface IEditingInteraction : IMouseControlHandler
    {
        void EndInteraction();
    }

    private class MouseControlHand : IMouseControlHandler
    {
        private bool _pressed;
        private AvaPoint _position;

        public required PlayerView View { get; init; }

        public required PlayerViewModel ViewModel { get; init; }

        private Player Player => View.Player;

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

    private readonly WeakReference<Drawable?> _lastSelected = new(null);
    private IMouseControlHandler? _mouseState;
    private int _lastMouseMode = -1;

    private void FinishEditingInteraction()
    {
        if (_mouseState is not IEditingInteraction handler) return;
        // Release capture only after detaching the handler, so CaptureLost cannot roll it back.
        _mouseState = null;
        handler.EndInteraction();
    }

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

    private void ZoomFrameAtPointer(PointerWheelEventArgs e, PlayerViewModel viewModel)
    {
        const float ZoomSpeed = 1.2f;
        AvaPoint pos = e.GetPosition(framePanel);
        float x = (float)pos.X, y = (float)pos.Y;
        float ratio = MathF.Pow(ZoomSpeed, (float)e.Delta.Y);
        var zoom = new Matrix(ratio, 0, 0, ratio, x - ratio * x, y - ratio * y);
        viewModel.FrameMatrix.Value = zoom * viewModel.FrameMatrix.Value;
        e.Handled = true;
    }

    private void OnFramePointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is PlayerViewModel viewModel)
        {
            if (viewModel.PathEditor.IsVisible.Value)
            {
                if ((e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0)
                    ZoomFrameAtPointer(e, viewModel);
                else
                {
                    AvaPoint delta = e.KeyModifiers.HasFlag(KeyModifiers.Shift)
                        ? new AvaPoint(e.Delta.Y, e.Delta.X) : new AvaPoint(e.Delta.X, e.Delta.Y);
                    viewModel.FrameMatrix.Value *= Matrix.CreateTranslation((float)delta.X * 24, (float)delta.Y * 24);
                }
                e.Handled = true;
                return;
            }

            if (viewModel.IsHandMode.Value)
                ZoomFrameAtPointer(e, viewModel);
            // During a drag (a held left or right button) the wheel step would commit the drag's unfinished edit
            // with its own.
            else if (viewModel.IsCameraMode.Value
                     && e.GetCurrentPoint(null).Properties is { IsLeftButtonPressed: false, IsRightButtonPressed: false })
                CreateCameraHandler(viewModel).OnWheelChanged(e);
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
        if (viewModel.IsHandMode.Value)
        {
            return new MouseControlHand { ViewModel = viewModel, View = this };
        }
        else if (viewModel.IsCameraMode.Value)
        {
            return CreateCameraHandler(viewModel);
        }
        else
        {
            return new MouseControlCrop { ViewModel = viewModel, View = this };
        }
    }

    private MouseControl3DCamera CreateCameraHandler(PlayerViewModel viewModel)
    {
        return new MouseControl3DCamera
        {
            ViewModel = viewModel,
            Clock = viewModel.EditViewModel.GetRequiredService<IEditorClock>(),
            EditorSelection = viewModel.EditViewModel.GetRequiredService<IEditorSelection>(),
            View = this
        };
    }

    private MouseControlTransformHandles CreateTransformHandlesHandler(
        PlayerViewModel viewModel, TransformHandlesOverlay.HandleKind kind)
    {
        return new MouseControlTransformHandles
        {
            View = this,
            ViewModel = viewModel,
            Clock = viewModel.EditViewModel.GetRequiredService<IEditorClock>(),
            EditorSelection = viewModel.EditViewModel.GetRequiredService<IEditorSelection>(),
            Kind = kind,
        };
    }

    private void OnFramePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(null);
        if (DataContext is PlayerViewModel viewModel)
        {
            if (viewModel.IsCameraMode.Value)
            {
                if (viewModel.IsPlaybackActive)
                {
                    FinishEditingInteraction();
                    e.Handled = true;
                    return;
                }

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
                    // Playback owns the render dispatcher until its producer exits. A synchronous
                    // hit-test here would block the UI until playback ends.
                    if (viewModel.IsPlaybackActive)
                    {
                        e.Handled = true;
                        return;
                    }

                    AvaPoint imagePoint = e.GetCurrentPoint(image).Position;
                    if (transformHandlesOverlay.HitTestEditingHandle(imagePoint) is { } editingHandle)
                    {
                        _mouseState = new MouseControlEditingHandle(this, viewModel, editingHandle.Provider, editingHandle.Handle);
                        _mouseState.OnPressed(e);
                        framePanel.Focus();
                        return;
                    }
                    TransformHandlesOverlay.HandleKind kind = transformHandlesOverlay.HitTest(imagePoint);
                    var handler = CreateTransformHandlesHandler(viewModel, kind);
                    _mouseState = handler;
                    handler.OnPressed(e);
                    // A press with no hit-test answer keeps the drawable the handles follow.
                    if (!handler.TimedOut)
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
