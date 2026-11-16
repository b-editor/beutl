using Avalonia.Input;
using Beutl.Logging;
using Microsoft.Extensions.Logging;
using RoyalTerminal.Avalonia.Controls;
using RoyalTerminal.Avalonia.Services;
using RoyalTerminal.Terminal;

namespace Beutl.Editor.Components.TerminalTab.Views;

internal sealed class ShellTerminalControl : TerminalControl
{
    public ShellTerminalControl()
    {
        MouseShiftCapturePolicy = TerminalMouseShiftCapturePolicy.Never;
        // Leave plain Ctrl+V available to the shell (quoted insert).
        ShortcutConfiguration = new TerminalShortcutConfiguration(pasteGestures:
        [
            new(Key.V, KeyModifiers.Meta),
            new(Key.V, KeyModifiers.Control | KeyModifiers.Shift),
            new(Key.Insert, KeyModifiers.Shift),
        ]);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // RoyalTerminal gives Ctrl+C interrupts priority over its built-in clipboard shortcuts.
        if (e.Key == Key.C && HasSelection
            && e.KeyModifiers is KeyModifiers.Control or (KeyModifiers.Control | KeyModifiers.Shift))
        {
            e.Handled = true;
            _ = CopySelectionOrPasteAsync();
            return;
        }

        base.OnKeyDown(e);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        // TUIs that request mouse reports own the click. Shift reserves it for local selection.
        bool reportsMouse = TerminalSessionService.ModeSource is ITerminalMouseReportingStateSource { MouseReportingEnabled: true };
        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind == PointerUpdateKind.RightButtonPressed
            && (!reportsMouse || e.KeyModifiers.HasFlag(KeyModifiers.Shift)))
        {
            Focus();
            e.Handled = true;
            _ = CopySelectionOrPasteAsync();
            return;
        }

        base.OnPointerPressed(e);
    }

    private async Task CopySelectionOrPasteAsync()
    {
        try
        {
            if (HasSelection)
            {
                await CopySelectionAsync();
                ClearSelection();
            }
            else
            {
                await PasteAsync();
            }
        }
        catch (Exception ex)
        {
            Log.CreateLogger<ShellTerminalControl>().LogWarning(ex, "Could not access the terminal clipboard");
        }
    }
}
