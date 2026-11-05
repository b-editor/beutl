using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Threading;

using Beutl.Configuration;

using FluentAvalonia.UI.Controls;

namespace Beutl.Controls.NativeContextMenu;

// Shows context menus and context flyouts as native menus on macOS, unless the setting turns that off. The setting is read
// each time a menu opens, so that it takes effect on the next menu. The Avalonia menu still opens, unseen: its bindings
// resolve, its events fire and IsOpen stays true as long as the native menu is up, so that the code around a menu does
// not notice. A menu that a native menu cannot show just becomes visible.
[ExcludeFromCodeCoverage(Justification = "Runs against AppKit only, which macOS-only tests cover; CI collects coverage on Linux.")]
internal static class NativeContextMenus
{
    private static readonly Dictionary<AvaloniaObject, Session> s_sessions = [];
    private static bool s_installed;

    private static bool IsEnabled => GlobalConfiguration.Instance.ViewConfig.UseNativeContextMenus.Value;

    public static void Install()
    {
        if (!OperatingSystem.IsMacOS() || s_installed)
            return;

        s_installed = true;
        // Both properties change right before the popup opens, when the flyout has placed it.
        FlyoutBase.IsOpenProperty.Changed.AddClassHandler<PopupFlyoutBase>(OnFlyoutIsOpenChanged);
        MenuBase.IsOpenProperty.Changed.AddClassHandler<ContextMenu>(OnContextMenuIsOpenChanged);
    }

    [SupportedOSPlatform("macos")]
    private static void OnFlyoutIsOpenChanged(PopupFlyoutBase flyout, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true)
        {
            End(flyout);
        }
        // Setting IsOpen directly opens the popup before this runs, and it has been seen by then.
        else if (IsEnabled
                 && flyout.Target is { } target
                 && ReferenceEquals(target.ContextFlyout, flyout)
                 && !flyout.Popup.IsOpen
                 && GetReader(flyout) is { } read)
        {
            Begin(flyout, flyout.Popup, target, read, flyout.Hide);
        }
    }

    private static Func<IReadOnlyList<ContextMenuEntry>?>? GetReader(PopupFlyoutBase flyout) => flyout switch
    {
        FAMenuFlyout or MenuFlyout when flyout.Popup.Child is ItemsControl presenter => () => ContextMenuEntry.TryRead(presenter),
        FACommandBarFlyout commandBar => () => ContextMenuEntry.TryRead(commandBar),
        _ => null
    };

    [SupportedOSPlatform("macos")]
    private static void OnContextMenuIsOpenChanged(ContextMenu menu, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true)
        {
            End(menu);
        }
        else if (IsEnabled && menu.Parent is Popup { PlacementTarget: { } target, IsOpen: false } popup)
        {
            Begin(menu, popup, target, () => ContextMenuEntry.TryRead(menu), menu.Close);
        }
    }

    [SupportedOSPlatform("macos")]
    private static void Begin(
        AvaloniaObject owner, Popup popup, Control target, Func<IReadOnlyList<ContextMenuEntry>?> read, Action close)
    {
        if (popup.Child is { } presenter
            && TopLevel.GetTopLevel(target) is { } topLevel
            && topLevel.TryGetPlatformHandle() is IMacOSTopLevelPlatformHandle { NSView: not 0 } handle)
        {
            var session = new Session(owner, popup, presenter, target, topLevel, handle, read, close);
            s_sessions[owner] = session;
            session.Begin();
        }
    }

    [SupportedOSPlatform("macos")]
    private static void End(AvaloniaObject owner)
    {
        if (s_sessions.Remove(owner, out Session? session))
            session.End();
    }

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_LastPointerPosition")]
    private static extern PixelPoint? GetLastPointerPosition(TopLevel topLevel);

    [SupportedOSPlatform("macos")]
    private sealed class Session(
        AvaloniaObject owner, Popup popup, Control presenter, Control target, TopLevel topLevel,
        IMacOSTopLevelPlatformHandle handle, Func<IReadOnlyList<ContextMenuEntry>?> read, Action close)
    {
        private readonly Dictionary<AvaloniaObject, ContextMenuEntry> _tracked = [];
        private MacOSContextMenu? _menu;
        private bool _ended;

        public void Begin()
        {
            // The layer spares the popup a window of its own. Of the top levels here, only a window has one.
            popup.ShouldUseOverlayLayer = topLevel is Window;
            presenter.Opacity = 0;
            presenter.IsHitTestVisible = false;
            Dispatcher.UIThread.Post(Present, DispatcherPriority.Loaded);
        }

        public void End()
        {
            _ended = true;
            Untrack();
            _menu?.Cancel();
            Restore();
        }

        private void Present()
        {
            if (_ended) return;

            // Only layout turns the items into containers, and a pending render can hold it back.
            presenter.UpdateLayout();
            if (read() is not { Count: > 0 } entries)
            {
                Restore();
                return;
            }

            _menu = new MacOSContextMenu(entries, handle.GetNSViewRetained(), topLevel.ActualThemeVariant);
            Track(entries);
            var offset = new Point(popup.HorizontalOffset, popup.VerticalOffset);
            if (popup.Placement == PlacementMode.Pointer && GetLastPointerPosition(topLevel) is { } pointer)
                _menu.Show(topLevel.PointToClient(pointer) + offset, true, OnClosed);
            else
                _menu.Show(GetLocation(_menu.Size, offset), false, OnClosed);
        }

        private Point GetLocation(Size size, Point offset)
        {
            Rect rect = popup.PlacementRect ?? new Rect(target.Bounds.Size);
            Point origin = target.TranslatePoint(rect.Position, topLevel) ?? default;
            return ContextMenuLayout.GetLocation(
                popup.Placement, popup.PlacementAnchor, popup.PlacementGravity, new Rect(origin, rect.Size), size, offset);
        }

        // Like an Avalonia menu, it runs the chosen item's handlers before it closes.
        private void OnClosed(ContextMenuEntry? chosen)
        {
            Untrack();
            if (_ended) return;

            try
            {
                chosen?.Invoke();
            }
            finally
            {
                _ended = true;
                s_sessions.Remove(owner);
                close();
                Restore();
            }
        }

        private void Restore()
        {
            popup.ClearValue(Popup.ShouldUseOverlayLayerProperty);
            presenter.ClearValue(Visual.OpacityProperty);
            presenter.ClearValue(InputElement.IsHitTestVisibleProperty);
        }

        private void Track(IReadOnlyList<ContextMenuEntry> entries)
        {
            foreach (ContextMenuEntry entry in entries)
            {
                if (_tracked.TryAdd(entry.Source, entry))
                    entry.Source.PropertyChanged += OnItemPropertyChanged;

                Track(entry.Children);
            }
        }

        private void Untrack()
        {
            foreach (AvaloniaObject source in _tracked.Keys)
                source.PropertyChanged -= OnItemPropertyChanged;

            _tracked.Clear();
        }

        private void OnItemPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (ContextMenuEntry.IsTracked(e.Property) && sender is AvaloniaObject source && _tracked.TryGetValue(source, out ContextMenuEntry? entry))
                _menu?.Refresh(entry);
        }
    }
}
