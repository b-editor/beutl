using System.ComponentModel.DataAnnotations;
using Beutl.Language;

namespace Beutl.NodeGraph.Nodes;

// Member names are serialized connection identifiers. Keep the names passed to GraphNode's
// member factory methods stable and localize only the non-serialized display metadata.
internal static class NodePortDisplays
{
    private static DisplayAttribute Create(string key) => new()
    {
        Name = key,
        ResourceType = typeof(NodeGraphStrings)
    };

    public static DisplayAttribute CenterX => Create(nameof(NodeGraphStrings.Port_CenterX));
    public static DisplayAttribute CenterY => Create(nameof(NodeGraphStrings.Port_CenterY));
    public static DisplayAttribute CenterZ => Create(nameof(NodeGraphStrings.Port_CenterZ));
    public static DisplayAttribute Depth => Create(nameof(NodeGraphStrings.Port_Depth));
    public static DisplayAttribute Duration => Create(nameof(NodeGraphStrings.Port_Duration));
    public static DisplayAttribute Error => Create(nameof(NodeGraphStrings.Port_Error));
    public static DisplayAttribute Expression => Create(nameof(NodeGraphStrings.Port_Expression));
    public static DisplayAttribute False => Create(nameof(NodeGraphStrings.Port_False));
    public static DisplayAttribute Fill => Create(nameof(NodeGraphStrings.Port_Fill));
    public static DisplayAttribute Geometry => Create(nameof(NodeGraphStrings.Port_Geometry));
    public static DisplayAttribute Height => Create(nameof(NodeGraphStrings.Port_Height));
    public static DisplayAttribute Input => Create(nameof(NodeGraphStrings.Port_Input));
    public static DisplayAttribute Inputs => Create(nameof(NodeGraphStrings.Port_Inputs));
    public static DisplayAttribute Matrix => Create(nameof(NodeGraphStrings.Port_Matrix));
    public static DisplayAttribute Maximum => Create(nameof(NodeGraphStrings.Port_Maximum));
    public static DisplayAttribute Minimum => Create(nameof(NodeGraphStrings.Port_Minimum));
    public static DisplayAttribute Output => Create(nameof(NodeGraphStrings.Port_Output));
    public static DisplayAttribute Pen => Create(nameof(NodeGraphStrings.Port_Pen));
    public static DisplayAttribute PixelPoint => Create(nameof(NodeGraphStrings.Port_PixelPoint));
    public static DisplayAttribute PixelRect => Create(nameof(NodeGraphStrings.Port_PixelRect));
    public static DisplayAttribute PixelSize => Create(nameof(NodeGraphStrings.Port_PixelSize));
    public static DisplayAttribute Point => Create(nameof(NodeGraphStrings.Port_Point));
    public static DisplayAttribute Position => Create(nameof(NodeGraphStrings.Port_Position));
    public static DisplayAttribute Preview => Create(nameof(NodeGraphStrings.Port_Preview));
    public static DisplayAttribute Progress => Create(nameof(NodeGraphStrings.Port_Progress));
    public static DisplayAttribute Rect => Create(nameof(NodeGraphStrings.Port_Rect));
    public static DisplayAttribute RelativePoint => Create(nameof(NodeGraphStrings.Port_RelativePoint));
    public static DisplayAttribute RelativeRect => Create(nameof(NodeGraphStrings.Port_RelativeRect));
    public static DisplayAttribute Rotation => Create(nameof(NodeGraphStrings.Port_Rotation));
    public static DisplayAttribute RotationX => Create(nameof(NodeGraphStrings.Port_RotationX));
    public static DisplayAttribute RotationY => Create(nameof(NodeGraphStrings.Port_RotationY));
    public static DisplayAttribute RotationZ => Create(nameof(NodeGraphStrings.Port_RotationZ));
    public static DisplayAttribute Scale => Create(nameof(NodeGraphStrings.Port_Scale));
    public static DisplayAttribute ScaleX => Create(nameof(NodeGraphStrings.Port_ScaleX));
    public static DisplayAttribute ScaleY => Create(nameof(NodeGraphStrings.Port_ScaleY));
    public static DisplayAttribute Size => Create(nameof(NodeGraphStrings.Port_Size));
    public static DisplayAttribute SkewX => Create(nameof(NodeGraphStrings.Port_SkewX));
    public static DisplayAttribute SkewY => Create(nameof(NodeGraphStrings.Port_SkewY));
    public static DisplayAttribute Source => Create(nameof(NodeGraphStrings.Port_Source));
    public static DisplayAttribute Start => Create(nameof(NodeGraphStrings.Port_Start));
    public static DisplayAttribute Switch => Create(nameof(NodeGraphStrings.Port_Switch));
    public static DisplayAttribute Time => Create(nameof(NodeGraphStrings.Port_Time));
    public static DisplayAttribute TopLeft => Create(nameof(NodeGraphStrings.Port_TopLeft));
    public static DisplayAttribute True => Create(nameof(NodeGraphStrings.Port_True));
    public static DisplayAttribute Unit => Create(nameof(NodeGraphStrings.Port_Unit));
    public static DisplayAttribute Value => Create(nameof(NodeGraphStrings.Port_Value));
    public static DisplayAttribute Width => Create(nameof(NodeGraphStrings.Port_Width));
    public static DisplayAttribute X => Create(nameof(NodeGraphStrings.Port_X));
    public static DisplayAttribute Y => Create(nameof(NodeGraphStrings.Port_Y));
}
