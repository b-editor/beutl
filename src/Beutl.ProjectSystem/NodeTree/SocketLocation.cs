namespace Beutl.NodeTree;

[Flags]
public enum SocketLocation
{
    Left = 0b1,
    Right = 0b10,
    Both = Left | Right
}
