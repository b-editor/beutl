using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;
using Beutl.Api.Services;
using Beutl.Editor.Components.Helpers;
using Beutl.Services.PrimitiveImpls;
using Beutl.ViewModels;
using Dock.Avalonia.Controls;

namespace Beutl.Views.Dock;

// The window a tool floats into. In the main window, a key that a tool leaves reaches the editor's commands and then
// MainView's, and this window hands such a key to the same commands. On macOS it also shows the main window's menu bar,
// which AppKit otherwise replaces with the application menu alone while this window has the keyboard.
internal sealed class FloatingToolWindow : HostWindow
{
    private readonly EditViewModel _editor;
    private readonly CompositeDisposable _menuSubscriptions = [];
    private IDisposable? _selectionSubscription;
    private Window? _ownerBeforeHide;

    public FloatingToolWindow(EditViewModel editor)
    {
        _editor = editor;
        // After this window's own handlers, so that Tab keeps moving the focus within it and the key that closes a
        // secondary window keeps closing its tab.
        AddHandler(KeyDownEvent, OnKeyDownLeftByTools);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _selectionSubscription ??= _editor.EditorService.IsSelectedEditor(_editor)
            .Subscribe(UpdateSceneVisibility);
        if (OperatingSystem.IsMacOS()
            && NativeMenu.GetMenu(this) is null
            && AppHelper.GetTopLevel()?.DataContext is MainViewModel main)
        {
            // Built for this window, so that the Edit entries act on the control with its focus.
            var menu = new NativeMenu();
            MainMenuBuilder.AddMenus(menu, main.MenuItems, this, _menuSubscriptions);
            NativeMenu.SetMenu(this, menu);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _selectionSubscription?.Dispose();
        _selectionSubscription = null;
        _ownerBeforeHide = null;
        // The menus follow lists that outlive the window.
        _menuSubscriptions.Dispose();
        base.OnClosed(e);
    }

    private void UpdateSceneVisibility(bool selected)
    {
        if (!selected)
        {
            _ownerBeforeHide = Owner as Window;
            Hide();
        }
        else if (!IsVisible)
        {
            // Keep the focus in the main window during keyboard tab switching. Avalonia clears Owner
            // on Hide; use the saved owner while the main DockControl is detached for the scene switch.
            bool showActivated = ShowActivated;
            SetCurrentValue(ShowActivatedProperty, false);
            try
            {
                if (_ownerBeforeHide is { } owner)
                    Show(owner);
                else
                    Show();
            }
            finally
            {
                SetCurrentValue(ShowActivatedProperty, showActivated);
            }
        }
    }

    private void OnKeyDownLeftByTools(object? sender, KeyEventArgs e)
    {
        // A dialog keeps its keys, as in the main window, whose overlays lie outside MainView.
        if ((e.Source as Visual)?.FindAncestorOfType<OverlayLayer>() is not null
            || AppHelper.GetContextCommandManager?.Invoke() is not { } commands)
        {
            return;
        }

        // The main window's editor view takes no keys while the editor is suspended.
        if (_editor.IsEnabled.Value)
            commands.HandleKeyDown(_editor.Extension, _editor, e);

        if (e.Handled
            || AppHelper.GetTopLevel() is not { DataContext: MainViewModel main } mainWindow
            || IsExitKey(commands, e))
        {
            return;
        }

        bool wasOpen = IsOverlayOpen(main);
        commands.HandleKeyDown(MainViewExtension.Instance, main, e);
        // The tab switcher and the command palette open in the main window and take its keys, such as the release of
        // the modifier that ends the switcher.
        if (!wasOpen && IsOverlayOpen(main) && mainWindow is WindowBase window)
            window.Activate();
    }

    // Exit stands for closing the main window. In this window its key, Alt+F4 on Windows, closes this window instead.
    private static bool IsExitKey(ContextCommandManager commands, KeyEventArgs e)
        => commands.GetDefinitions<MainViewExtension>()
                .FirstOrDefault(entry => entry.Definition.Id == MainViewExtension.ExitCommandId) is { } exit
            && ContextCommandManager.Matches(exit, e);

    private static bool IsOverlayOpen(MainViewModel main)
        => main.TabSwitcher.IsOpen.Value || main.CommandPalette.IsOpen.Value;
}
