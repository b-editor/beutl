namespace Beutl.Graphics3D;

public interface IVertexType
{
    static abstract VertexElementFormat[] Formats { get; }

    static abstract uint[] Offsets { get; }
}
