namespace Beutl.NodeGraph;

[Flags]
public enum NodePortLocation
{
    Left = 0b1,
    Right = 0b10,
    Both = Left | Right
}
