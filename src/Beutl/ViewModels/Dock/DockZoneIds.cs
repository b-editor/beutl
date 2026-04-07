namespace Beutl.ViewModels.Dock;

/// <summary>
/// Identifiers for the Tool dock zones that make up the default editor layout.
/// Used as <c>IDockable.Id</c> values so tools can be located by <see cref="ToolTabExtension.TabPlacement"/>.
/// </summary>
internal static class DockZoneIds
{
    public const string Root = "Zone.Root";
    public const string Player = "Zone.Player";

    public const string LeftUpperTop = "Zone.LeftUpperTop";
    public const string LeftUpperBottom = "Zone.LeftUpperBottom";
    public const string LeftLowerTop = "Zone.LeftLowerTop";
    public const string LeftLowerBottom = "Zone.LeftLowerBottom";
    public const string RightUpperTop = "Zone.RightUpperTop";
    public const string RightUpperBottom = "Zone.RightUpperBottom";
    public const string RightLowerTop = "Zone.RightLowerTop";
    public const string RightLowerBottom = "Zone.RightLowerBottom";

    // Proportional dock ids (for saving/restoring split proportions)
    public const string LeftColumn = "Split.LeftColumn";
    public const string RightColumn = "Split.RightColumn";
    public const string CenterColumn = "Split.CenterColumn";
    public const string CenterTopRow = "Split.CenterTopRow";
    public const string CenterBottomRow = "Split.CenterBottomRow";
    public const string TopHorizontal = "Split.TopHorizontal";

    public static string FromPlacement(ToolTabExtension.TabPlacement placement) => placement switch
    {
        ToolTabExtension.TabPlacement.LeftUpperTop => LeftUpperTop,
        ToolTabExtension.TabPlacement.LeftUpperBottom => LeftUpperBottom,
        ToolTabExtension.TabPlacement.LeftLowerTop => LeftLowerTop,
        ToolTabExtension.TabPlacement.LeftLowerBottom => LeftLowerBottom,
        ToolTabExtension.TabPlacement.RightUpperTop => RightUpperTop,
        ToolTabExtension.TabPlacement.RightUpperBottom => RightUpperBottom,
        ToolTabExtension.TabPlacement.RightLowerTop => RightLowerTop,
        ToolTabExtension.TabPlacement.RightLowerBottom => RightLowerBottom,
        _ => LeftUpperTop,
    };

    public static ToolTabExtension.TabPlacement? ToPlacement(string? zoneId) => zoneId switch
    {
        LeftUpperTop => ToolTabExtension.TabPlacement.LeftUpperTop,
        LeftUpperBottom => ToolTabExtension.TabPlacement.LeftUpperBottom,
        LeftLowerTop => ToolTabExtension.TabPlacement.LeftLowerTop,
        LeftLowerBottom => ToolTabExtension.TabPlacement.LeftLowerBottom,
        RightUpperTop => ToolTabExtension.TabPlacement.RightUpperTop,
        RightUpperBottom => ToolTabExtension.TabPlacement.RightUpperBottom,
        RightLowerTop => ToolTabExtension.TabPlacement.RightLowerTop,
        RightLowerBottom => ToolTabExtension.TabPlacement.RightLowerBottom,
        _ => null,
    };

    public static readonly string[] AllToolZones =
    [
        LeftUpperTop, LeftUpperBottom, LeftLowerTop, LeftLowerBottom,
        RightUpperTop, RightUpperBottom, RightLowerTop, RightLowerBottom,
    ];
}
