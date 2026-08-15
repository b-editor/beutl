namespace Beutl.Editor.Components.Helpers;

/// <summary>
/// Picks which open tool tab a "show this object" request should land on.
/// </summary>
internal static class ToolTabReuse
{
    /// <summary>
    /// Returns the tab to retarget, or <see langword="null"/> when the caller must create one.
    /// </summary>
    /// <param name="isExactMatch">Matches a tab already showing the requested object.</param>
    /// <param name="isIdle">Matches a tab with no object yet.</param>
    /// <param name="retargetAnyOpen">
    /// Whether an occupied tab may be taken over as a last resort. True for tools reachable from the
    /// "add tool tab" menu, where dropping straight to a new tab would spawn one per object and the
    /// user can still open a second tab by hand. False for a tool with no
    /// <see cref="ToolTabExtension.Header"/>, where taking over the only tab would make a second
    /// instance unreachable.
    /// </param>
    public static T? Find<T>(
        IEditorContext editorContext,
        Func<T, bool> isExactMatch,
        Func<T, bool> isIdle,
        bool retargetAnyOpen)
        where T : IToolContext
    {
        // Order matters: a plain FindToolTab always returns the first tab, which would strand every
        // other instance.
        return editorContext.FindToolTab(isExactMatch)
               ?? editorContext.FindToolTab(isIdle)
               ?? (retargetAnyOpen ? editorContext.FindToolTab<T>() : default);
    }
}
