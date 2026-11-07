using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using Avalonia;
using Avalonia.Styling;
using Avalonia.Threading;

using static Beutl.Controls.ObjCRuntime;

namespace Beutl.Controls.NativeContextMenu;

// An NSMenu made from the entries. AppKit tracks a menu in a nested run loop until it closes, and the dispatcher keeps
// running in it. Tracking starts from a run loop source of its own: inside a dispatcher job it would stall the
// main-queue block or the observer the job runs in, and with it what Avalonia.Native wakes the dispatcher with.
[SupportedOSPlatform("macos")]
[ExcludeFromCodeCoverage(Justification = "AppKit interop is covered by macOS-only tests; CI collects coverage on Linux.")]
internal sealed partial class MacOSContextMenu
{
    private const string LibObjC = "/usr/lib/libobjc.A.dylib";

    private static readonly nint s_itemInvoked = Selector("beutlMenuItemInvoked:");
    private static readonly nint s_startTracking = Selector("beutlStartTracking:");
    private static readonly nint s_target = CreateTarget();
    private static readonly nint s_blankImage = InitWithSize(
        Send(GetClass("NSImage"), Selector("alloc")), Selector("initWithSize:"), new NativeSize(ContextMenuIcons.Size, ContextMenuIcons.Size));
    private static readonly Queue<MacOSContextMenu> s_waiting = [];
    private static MacOSContextMenu? s_current;

    private readonly Dictionary<ContextMenuEntry, nint> _items = [];
    private readonly List<ContextMenuEntry> _invokable = [];
    private readonly nint _view;
    private readonly nint _menu;
    private Action<ContextMenuEntry?>? _closed;
    private ContextMenuEntry? _chosen;
    private NativePoint _location;
    private bool _atPointer;
    private bool _isTracking;
    private bool _isCanceled;

    // The view is retained for the life of the menu, which may outlast the window.
    public MacOSContextMenu(IReadOnlyList<ContextMenuEntry> entries, nint retainedView, ThemeVariant? theme)
    {
        _view = retainedView;
        nint pool = Send(GetClass("NSAutoreleasePool"), Selector("new"));
        try
        {
            _menu = CreateMenu(entries);
            string appearance = IsDark(theme) ? "NSAppearanceNameDarkAqua" : "NSAppearanceNameAqua";
            SendVoid(_menu, Selector("setAppearance:"), Send(GetClass("NSAppearance"), Selector("appearanceNamed:"), AutoreleasedString(appearance)));
        }
        finally
        {
            SendVoid(pool, Selector("drain"));
        }
    }

    private static bool IsDark(ThemeVariant? theme)
    {
        for (; theme is not null; theme = theme.InheritVariant)
        {
            if (theme == ThemeVariant.Dark) return true;
        }

        return false;
    }

    // The tracked menu and whether AppKit is tracking it, for tests.
    internal static MacOSContextMenu? Current => s_current;

    internal nint Handle => _menu;

    internal bool IsTracking => _isTracking;

    public Size Size
    {
        get
        {
            NativeSize size = SendSize(_menu, Selector("size"));
            return new Size(size.Width, size.Height);
        }
    }

    // A menu at the pointer is a context menu to AppKit, which opens it upward near the bottom of the screen. Otherwise
    // the location is where the menu's top left corner goes.
    public void Show(Point location, bool atPointer, Action<ContextMenuEntry?> closed)
    {
        _location = new NativePoint(location.X, location.Y);
        _atPointer = atPointer;
        _closed = closed;
        s_waiting.Enqueue(this);
        StartNext();
    }

    // A menu that was canceled can still be up when the next one is shown.
    private static void StartNext()
    {
        if (s_current is null && s_waiting.TryDequeue(out s_current))
            PerformOnMainThread(s_target, Selector("performSelectorOnMainThread:withObject:waitUntilDone:"), s_startTracking, 0, false);
    }

    public void Cancel()
    {
        _isCanceled = true;
        if (_isTracking) SendVoid(_menu, Selector("cancelTracking"));
    }

    public void Refresh(ContextMenuEntry entry)
    {
        if (!_items.TryGetValue(entry, out nint item)) return;

        nint pool = Send(GetClass("NSAutoreleasePool"), Selector("new"));
        try
        {
            ApplyState(entry, item);
        }
        finally
        {
            SendVoid(pool, Selector("drain"));
        }
    }

    private nint CreateMenu(IReadOnlyList<ContextMenuEntry> entries)
    {
        nint menu = Send(GetClass("NSMenu"), Selector("new"));
        // Every item says itself whether it is enabled, and a target that always responds would enable them all.
        SendBool(menu, Selector("setAutoenablesItems:"), false);
        SendBool(menu, Selector("setAllowsContextMenuPlugIns:"), false);
        bool hasIcons = entries.Any(entry => entry.Icon is not null);
        foreach (ContextMenuEntry entry in entries)
        {
            nint item = CreateItem(entry, hasIcons);
            SendVoid(menu, Selector("addItem:"), item);
            SendVoid(item, Selector("release"));
        }

        return menu;
    }

    // Returns an owned item. Among items with icons, one without gets a blank one, or its text starts where the icons do.
    private nint CreateItem(ContextMenuEntry entry, bool hasIcons)
    {
        nint item;
        if (entry.Kind == ContextMenuEntryKind.Separator)
        {
            item = Send(Send(GetClass("NSMenuItem"), Selector("separatorItem")), Selector("retain"));
        }
        else
        {
            string? key = entry.Gesture is { } gesture ? ContextMenuLayout.GetKeyEquivalent(gesture.Key) : null;
            item = Send(
                Send(GetClass("NSMenuItem"), Selector("alloc")),
                Selector("initWithTitle:action:keyEquivalent:"),
                AutoreleasedString(entry.Title),
                entry.Kind == ContextMenuEntryKind.Item ? s_itemInvoked : 0,
                AutoreleasedString(key ?? string.Empty));
            if (key is not null)
                SendVoid(item, Selector("setKeyEquivalentModifierMask:"), (nint)ContextMenuLayout.GetModifierMask(entry.Gesture!.KeyModifiers));

            if (entry.Icon is { } icon && ContextMenuIcons.TryRender(icon) is { } png)
                SetImage(item, png, ContextMenuIcons.IsTemplate(icon));
            else if (hasIcons)
                SendVoid(item, Selector("setImage:"), s_blankImage);

            if (entry.Kind == ContextMenuEntryKind.Item)
            {
                SendVoid(item, Selector("setTarget:"), s_target);
                SendVoid(item, Selector("setTag:"), _invokable.Count);
                _invokable.Add(entry);
            }
            else
            {
                nint subMenu = CreateMenu(entry.Children);
                SendVoid(item, Selector("setSubmenu:"), subMenu);
                SendVoid(subMenu, Selector("release"));
            }
        }

        _items.Add(entry, item);
        ApplyState(entry, item);
        return item;
    }

    private static void ApplyState(ContextMenuEntry entry, nint item)
    {
        SendBool(item, Selector("setHidden:"), !entry.IsVisible);
        if (entry.Kind == ContextMenuEntryKind.Separator) return;

        SendVoid(item, Selector("setTitle:"), AutoreleasedString(entry.Title));
        SendBool(item, Selector("setEnabled:"), entry.IsEnabled && (entry.Kind == ContextMenuEntryKind.Item || entry.Children.Count > 0));
        SendVoid(item, Selector("setState:"), entry.IsChecked ? 1 : 0);
    }

    private static unsafe void SetImage(nint item, byte[] png, bool isTemplate)
    {
        nint data;
        fixed (byte* bytes = png)
        {
            data = Send(GetClass("NSData"), Selector("dataWithBytes:length:"), (nint)bytes, png.Length);
        }

        nint image = Send(Send(GetClass("NSImage"), Selector("alloc")), Selector("initWithData:"), data);
        if (image == 0) return;

        SendSize(image, Selector("setSize:"), new NativeSize(ContextMenuIcons.Size, ContextMenuIcons.Size));
        SendBool(image, Selector("setTemplate:"), isTemplate);
        SendVoid(item, Selector("setImage:"), image);
        SendVoid(image, Selector("release"));
    }

    private void Track()
    {
        if (!_isCanceled)
        {
            _isTracking = true;
            if (_atPointer)
                PopUpContextMenu(GetClass("NSMenu"), Selector("popUpContextMenu:withEvent:forView:"), _menu, CreateContextEvent(), _view);
            else
                PopUp(_menu, Selector("popUpMenuPositioningItem:atLocation:inView:"), 0, _location, _view);
            _isTracking = false;
        }

        // There is no synchronization context here, and the handlers of an item may await.
        Dispatcher.UIThread.Post(Finish, DispatcherPriority.Send);
    }

    private nint CreateContextEvent()
    {
        const ulong RightMouseDown = 3;
        nint currentEvent = CurrentEvent();
        return MouseEvent(
            GetClass("NSEvent"),
            Selector("mouseEventWithType:location:modifierFlags:timestamp:windowNumber:context:eventNumber:clickCount:pressure:"),
            RightMouseDown,
            ConvertPoint(_view, Selector("convertPoint:toView:"), _location, 0),
            0,
            currentEvent != 0 ? SendDouble(currentEvent, Selector("timestamp")) : 0,
            Send(Send(_view, Selector("window")), Selector("windowNumber")),
            0,
            0,
            1,
            1);
    }

    private void Finish()
    {
        s_current = null;
        Action<ContextMenuEntry?>? closed = _closed;
        ContextMenuEntry? chosen = _chosen;
        _closed = null;
        SendVoid(_menu, Selector("release"));
        SendVoid(_view, Selector("release"));
        StartNext();
        closed?.Invoke(chosen);
    }

    private static unsafe nint CreateTarget()
    {
        nint type = objc_allocateClassPair(GetClass("NSObject"), "BeutlContextMenuTarget", 0);
        class_addMethod(type, s_itemInvoked, (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&OnItemInvoked, "v@:@");
        class_addMethod(type, s_startTracking, (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&OnStartTracking, "v@:@");
        objc_registerClassPair(type);
        return Send(type, Selector("new"));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnItemInvoked(nint self, nint selector, nint item)
    {
        if (s_current is { } current)
        {
            int index = (int)Send(item, Selector("tag"));
            if (index >= 0 && index < current._invokable.Count)
                current._chosen = current._invokable[index];
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnStartTracking(nint self, nint selector, nint argument)
    {
        try
        {
            s_current?.Track();
        }
        catch
        {
            // Exceptions must not escape a native callback. The menu then counts as dismissed.
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NativePoint(double X, double Y);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NativeSize(double Width, double Height);

    [LibraryImport(LibObjC, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint objc_allocateClassPair(nint superclass, string name, nuint extraBytes);

    [LibraryImport(LibObjC)]
    private static partial void objc_registerClassPair(nint type);

    [LibraryImport(LibObjC, StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static partial bool class_addMethod(nint type, nint selector, nint implementation, string signature);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static partial void SendBool(nint receiver, nint selector, [MarshalAs(UnmanagedType.I1)] bool value);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static partial NativeSize SendSize(nint receiver, nint selector);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static partial void SendSize(nint receiver, nint selector, NativeSize size);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static partial nint InitWithSize(nint receiver, nint selector, NativeSize size);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static partial NativePoint ConvertPoint(nint receiver, nint selector, NativePoint point, nint view);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static partial nint MouseEvent(
        nint receiver, nint selector, ulong type, NativePoint location, ulong flags, double timestamp, nint windowNumber,
        nint context, nint eventNumber, nint clickCount, float pressure);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static partial void PopUpContextMenu(nint receiver, nint selector, nint menu, nint contextEvent, nint view);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static partial void PerformOnMainThread(
        nint receiver, nint selector, nint performed, nint argument, [MarshalAs(UnmanagedType.I1)] bool waitUntilDone);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static partial bool PopUp(nint receiver, nint selector, nint positioningItem, NativePoint location, nint view);
}
