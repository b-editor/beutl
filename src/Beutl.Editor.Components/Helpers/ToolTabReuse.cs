namespace Beutl.Editor.Components.Helpers;

internal static class ToolTabReuse
{
    /// <summary>
    /// Finds a matching, idle, or optionally occupied tab.
    /// </summary>
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
               ?? editorContext.FindToolTab(isIdle)
               ?? (retargetAnyOpen ? editorContext.FindToolTab<T>() : default);
    }
}
