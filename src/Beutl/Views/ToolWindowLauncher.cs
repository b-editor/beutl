using Avalonia.Controls;
using Beutl.Api.Services;
using Beutl.Services;

namespace Beutl.Views;

// Opens tool-window extensions for one main window; a single-instance window is activated instead of reopened.
internal sealed class ToolWindowLauncher
{
    private readonly Dictionary<ToolWindowExtension, Window> _openToolWindows = new();

    public async Task OpenAsync(ToolWindowExtension extension, Func<Window?> resolveOwner)
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

            if (ExtensionGuard.Call(extension, () => extension.TryCreateContext(out IToolWindowContext? created) ? created : null)
                is not { } context)
                return;

            if (ExtensionGuard.Call(extension, () => extension.TryCreateContent(out Window? created) ? created : null)
                is not { } window)
            {
                context.Dispose();
                return;
            }

            window.DataContext = context;
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
            await ex.Handle();
        }
    }
}
