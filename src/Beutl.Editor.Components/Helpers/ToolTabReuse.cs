namespace Beutl.Editor.Components.Helpers;

internal static class ToolTabReuse
{
    /// <summary>
    /// Finds a matching, idle, or optionally occupied tab.
    /// </summary>
    /// <remarks>
    /// A pinned tab is returned only when it already shows the target; it is never retargeted.
    /// </remarks>
    /// <param name="retargetAnyOpen">Whether to reuse an occupied tab when no match or idle tab exists.</param>
    public static T? Find<T>(
        IEditorContext editorContext,
        Func<T, bool> isExactMatch,
        Func<T, bool> isIdle,
        bool retargetAnyOpen)
        where T : IToolContext
    {
        // Prefer matching and idle tabs before occupied tabs.
        return editorContext.FindToolTab(isExactMatch)
               ?? editorContext.FindToolTab<T>(t => !IsPinned(t) && isIdle(t))
               ?? (retargetAnyOpen ? editorContext.FindToolTab<T>(t => !IsPinned(t)) : default);
    }

    public static bool IsPinned(IToolContext tab)
    {
        return tab is IPinnableToolContext { IsPinned.Value: true };
    }
}
