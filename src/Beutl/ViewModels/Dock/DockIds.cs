namespace Beutl.ViewModels.Dock;

internal static class DockIds
{
    public const string Root = "Root";
    public const string RootSplit = "Dock.Root";
    public const string Top = "Dock.Top";
    public const string Left = "Dock.Left";
    public const string Right = "Dock.Right";
    public const string Bottom = "Dock.Bottom";
    public const string Player = "Dock.Player";

    public static string? FromAnchor(DockAnchor anchor) => anchor switch
    {
        DockAnchor.Left => Left,
        DockAnchor.Right => Right,
        DockAnchor.Bottom => Bottom,
        DockAnchor.Top => Top,
        DockAnchor.Player => Player,
        _ => null,
    };
}
