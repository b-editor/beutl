using Avalonia.Controls;
using Beutl.Api.Services;
using Beutl.Controls;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.Helpers;
using Beutl.Services;

namespace Beutl.Views;

// Opens tool-window extensions for one main window; a single-instance window is activated instead of reopened.
internal sealed class ToolWindowLauncher
{
    private readonly Dictionary<ToolWindowExtension, Window> _openToolWindows = new();

    public async Task OpenAsync(ToolWindowExtension extension, Func<Window?> resolveOwner, INotificationService notifications)
    {
        try
        {
            if (resolveOwner() is not Window owner)
                return;

            if (extension.Mode == ToolWindowMode.Window
                && !extension.CanMultiple
                && _openToolWindows.TryGetValue(extension, out Window? existing))
            {
                existing.Activate();
                return;
            }

            if (ExtensionGuard.Call(extension, notifications, () => extension.TryCreateContext(out IToolWindowContext? created) ? created : null)
                is not { } context)
                return;

            if (ExtensionGuard.Call(extension, notifications, () => extension.TryCreateContent(out Window? created) ? created : null)
                is not { } window)
            {
                context.Dispose();
                return;
            }

            window.DataContext = context;
            ColorPaletteService.SetService(window, ColorPaletteService.GetService(owner));
            ControlConfiguration.SetConfiguration(window, ControlConfiguration.GetConfiguration(owner));
            ControlFonts.SetFonts(window, ControlFonts.GetFonts(owner));
            ControlNotifications.SetNotifications(window, ControlNotifications.GetNotifications(owner));
            if (string.IsNullOrEmpty(window.Title))
            {
                window.Title = context.Header;
            }

            switch (extension.Mode)
            {
                case ToolWindowMode.Dialog:
                    try
                    {
                        await window.ShowDialog(owner);
                    }
                    finally
                    {
                        context.Dispose();
                    }
                    break;

                case ToolWindowMode.Window:
                    if (!extension.CanMultiple)
                    {
                        _openToolWindows[extension] = window;
                    }

                    window.Closed += (_, _) =>
                    {
                        _openToolWindows.Remove(extension);
                        context.Dispose();
                    };
                    window.Show(owner);
                    break;
            }
        }
        catch (Exception ex)
        {
            await ex.Handle(notifications);
        }
    }
}
