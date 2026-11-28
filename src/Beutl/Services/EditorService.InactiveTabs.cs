using System.Diagnostics.CodeAnalysis;
using Avalonia.Threading;
using Beutl.ViewModels;
using Microsoft.Extensions.Logging;

namespace Beutl.Services;

public sealed partial class EditorService
{
    // The tab sets of the loaded sessions the editor does not show, by project.
    private readonly Dictionary<Project, InactiveTabSet> _inactiveTabs = new(ReferenceEqualityComparer.Instance);

    // The tabs of a session put aside while another one is active. The tabs stay whole, so the session's edits,
    // selection, undo history and unsaved sidecars come back with it. Selected is the tab that was selected.
    internal sealed record InactiveTabSet(EditorTabItem[] Items, EditorTabItem? Selected);

    // Every tab put aside, for checks that cover every editor this service owns.
    internal IEnumerable<EditorTabItem> InactiveTabItems => _inactiveTabs.Values.SelectMany(set => set.Items);

    internal bool HasInactiveTabs(Project project) => _inactiveTabs.ContainsKey(project);

    // Takes the open tabs out of the tab list and keeps them for project, which the editor stops showing. Their
    // playback stops; nothing is saved or disposed.
    internal async Task PutTabsAsideAsync(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        Dispatcher.UIThread.VerifyAccess();
        EditorTabItem[] items = [.. _tabItems];
        EditorTabItem? selected = SelectedTabItem.Value is { } tab && items.Contains(tab) ? tab : null;
        foreach (EditViewModel editor in items.Select(item => item.Context.Value).OfType<EditViewModel>())
        {
            if (editor.IsDisposingOrDisposed)
                continue;

            try
            {
                await editor.Player.Pause();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to pause an editor that is put aside.");
            }
        }

        SelectedTabItem.Value = null;
        _tabItems.Clear();
        if (_inactiveTabs.Remove(project, out InactiveTabSet? earlier))
        {
            // A session cannot be put aside twice; keep both sets rather than lose the earlier tabs.
            items = [.. earlier.Items, .. items];
            selected ??= earlier.Selected;
        }

        _inactiveTabs[project] = new InactiveTabSet(items, selected);
    }

    // Removes and returns the tabs put aside for project.
    internal bool TryTakeInactiveTabs(Project project, [NotNullWhen(true)] out InactiveTabSet? tabs)
    {
        ArgumentNullException.ThrowIfNull(project);
        return _inactiveTabs.Remove(project, out tabs);
    }

    // Puts back tabs taken with TryTakeInactiveTabs whose session stays inactive after all.
    internal void ReturnInactiveTabs(Project project, InactiveTabSet tabs)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(tabs);
        _inactiveTabs[project] = tabs;
    }

    // Removes and returns every tab put aside, for shutdown.
    internal EditorTabItem[] TakeAllInactiveTabs()
    {
        EditorTabItem[] items = [.. InactiveTabItems];
        _inactiveTabs.Clear();
        return items;
    }
}
