using Avalonia.Controls;
using Beutl.Services;

namespace Beutl.Views;

// Opens tool-window extensions for one main window; a single-instance window is activated instead of reopened.
internal sealed class ToolWindowLauncher
{
    private readonly Dictionary<ToolWindowExtension, List<Window>> _openToolWindows = new();

    public async Task OpenAsync(ToolWindowExtension extension, Func<Window?> resolveOwner)
    {
        try
        {
            if (resolveOwner() is not Window owner)
                return;

            // 非モーダル・単一インスタンスの場合、既存があればアクティブ化するだけ
            if (extension.Mode == ToolWindowMode.Window
                && !extension.CanMultiple
                && _openToolWindows.TryGetValue(extension, out List<Window>? existingList)
                && existingList.Count > 0)
            {
                existingList[0].Activate();
                return;
            }

            if (!extension.TryCreateContext(out IToolWindowContext? context))
                return;

            if (!extension.TryCreateContent(out Window? window))
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
                    if (!_openToolWindows.TryGetValue(extension, out List<Window>? list))
                    {
                        list = new List<Window>();
                        _openToolWindows[extension] = list;
                    }

                    list.Add(window);
                    window.Closed += (_, _) =>
                    {
                        list.Remove(window);
                        if (list.Count == 0)
                        {
                            _openToolWindows.Remove(extension);
                        }
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
