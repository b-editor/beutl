using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;
using Beutl.Api.Services;
using Beutl.Editor.Components.Helpers;
using Beutl.Services.PrimitiveImpls;
using Beutl.ViewModels;
using Beutl.ViewModels.SettingsPages;
using Dock.Avalonia.Controls;

namespace Beutl.Views.Dock;

// The window a tool floats into. In the main window, a key that a tool leaves reaches the editor's commands and then
// MainView's, and this window hands such a key to the same commands. On macOS it also shows the main window's menu bar,
// which AppKit otherwise replaces with the application menu alone while this window has the keyboard.
internal sealed class FloatingToolWindow : HostWindow
{
    private readonly IEditorContext _editor;
    private readonly CompositeDisposable _menuSubscriptions = [];

    public FloatingToolWindow(IEditorContext editor)
    {
        _editor = editor;
        // After this window's own handlers, so that Tab keeps moving the focus within it and the key that closes a
        // secondary window keeps closing its tab.
        AddHandler(KeyDownEvent, OnKeyDownLeftByTools);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
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
        // The menus follow lists that outlive the window.
        _menuSubscriptions.Dispose();
        base.OnClosed(e);
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
            .FirstOrDefault(entry => entry.Definition.Id == MainViewExtension.ExitCommandId)?.KeyGestures
            .Any(gesture => gesture.Platform == KeyMapSettingsItem.CurrentPlatform && gesture.KeyGesture?.Matches(e) == true)
            == true;

    private static bool IsOverlayOpen(MainViewModel main)
        => main.TabSwitcher.IsOpen.Value || main.CommandPalette.IsOpen.Value;
}
