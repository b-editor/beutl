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

        public void OnWheelChanged(PointerWheelEventArgs e) => View.ZoomFrameAtPointer(e, ViewModel);

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
