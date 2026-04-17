using Beutl.Graphics3D.Meshes;

namespace Beutl.Converters;

internal sealed class Vertex3DJsonConverter : StringJsonConverter<Vertex3D>
{
    protected override string TypeName => "Vertex3D";
    protected override Vertex3D Parse(string s) => Vertex3D.Parse(s);
}
