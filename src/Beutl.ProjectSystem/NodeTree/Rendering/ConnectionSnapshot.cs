namespace Beutl.NodeTree.Rendering;

public struct ConnectionSnapshot
{
    public int OutputSlotIndex;
    public int OutputItemIndex;
    public int InputSlotIndex;
    public int InputItemIndex;
    public Connection? OriginalConnection;
}
