namespace Beutl.Editor.Components.VersionControlTab.Views;

internal static class VersionControlTabLayout
{
    public const double WideLayoutMinimumWidth = 600;

    public static bool IsNarrow(double availableWidth)
    {
        return availableWidth < WideLayoutMinimumWidth;
    }
}
