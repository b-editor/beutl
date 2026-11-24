using Avalonia.Input;
using Avalonia.Interactivity;
using Beutl.Graphics;
using Beutl.ViewModels;
using AvaPoint = Avalonia.Point;
using BtlPoint = Beutl.Graphics.Point;

namespace Beutl.Views;

public partial class PlayerView
{
    // While a tool tab waits for a point of the frame, as the curves tab's eyedropper does, a left click on the frame
    // gives it and a right click cancels. The handlers tunnel, so the click reaches neither the handles nor the path
    // editor inside the panel, and a right click opens no menu. A middle drag still pans and the wheel still zooms.
    private void SetupPointPicking()
    {
        framePanel.AddHandler(PointerPressedEvent, OnPointPickPressed, RoutingStrategies.Tunnel);
        framePanel.AddHandler(PointerReleasedEvent, OnPointPickReleased, RoutingStrategies.Tunnel);
        framePanel.AddHandler(PointerMovedEvent, OnPointPickMoved, RoutingStrategies.Tunnel);
        framePanel.PointerExited += OnPointPickExited;
    }

    private PreviewPointPick? ActivePointPick => (DataContext as PlayerViewModel)?.PointPick.Value;

    private void OnPointPickPressed(object? sender, PointerPressedEventArgs e)
    {
        var model = DataContext as PlayerViewModel;
        var puppet = model?.PuppetPlacementEffect.Value;
        PreviewPointPick? pick = ActivePointPick;
        if (pick is null && puppet is null)
            return;

        PointerPointProperties properties = e.GetCurrentPoint(framePanel).Properties;
        if (properties.IsMiddleButtonPressed)
            return;

        // Pressing an existing Puppet pin returns to ordinary pin editing and lets this press start the drag.
        if (puppet is not null && properties.IsLeftButtonPressed
            && transformHandlesOverlay.HitTestEditingHandle(e.GetPosition(image)) is { } target
            && ReferenceEquals(target.Provider, puppet))
        {
            model!.StopPuppetPinPlacement();
            return;
        }

        // A click off the frame picks nothing and waits for another.
        if (pick is not null && (puppet is null || ReferenceEquals(puppet, pick.LocalOwner))
            && properties.IsLeftButtonPressed && FramePointUnder(e) is { } point
            && PointInPickSpace(pick, point) is { } local)
            pick.Complete(local);

        e.Handled = true;
    }

    private void OnPointPickReleased(object? sender, PointerReleasedEventArgs e)
    {
        var model = DataContext as PlayerViewModel;
        PreviewPointPick? pick = ActivePointPick;
        bool placingPins = model?.PuppetPlacementEffect.Value is not null;
        if ((pick is null && !placingPins) || e.InitialPressMouseButton == MouseButton.Middle)
            return;

        if (e.InitialPressMouseButton == MouseButton.Right)
        {
            if (placingPins) model!.StopPuppetPinPlacement();
            else pick?.Cancel();
        }

        e.Handled = true;
    }

    private void OnPointPickMoved(object? sender, PointerEventArgs e)
    {
        ActivePointPick?.Hover(FramePointUnder(e));
    }

    private void OnPointPickExited(object? sender, PointerEventArgs e)
    {
        ActivePointPick?.Hover(null);
    }

    private BtlPoint? PointInPickSpace(PreviewPointPick pick, BtlPoint point)
    {
        if (pick.LocalOwner is not IHierarchical owner) return point;

        Drawable? drawable = owner.FindHierarchicalParent<Drawable>();
        if (drawable is not null && !ReferenceEquals(drawable, transformHandlesOverlay.Drawable))
            UpdateTransformHandles();

        return drawable is not null && ReferenceEquals(drawable, transformHandlesOverlay.Drawable)
            && transformHandlesOverlay.UserMatrix.TryInvert(out var inverse)
            ? inverse.Transform(point)
            : null;
    }

    // The point of the frame under the pointer in frame pixels, or null off the frame.
    private BtlPoint? FramePointUnder(PointerEventArgs e)
    {
        if (ViewportTransform is not { } viewport)
            return null;

        AvaPoint point = viewport.ToFrame(e.GetPosition(image));
        return point.X >= 0 && point.Y >= 0 && point.X < viewport.FrameSize.Width && point.Y < viewport.FrameSize.Height
            ? new BtlPoint((float)point.X, (float)point.Y)
            : null;
    }
}
