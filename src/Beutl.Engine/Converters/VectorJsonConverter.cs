using Beutl.Graphics;

namespace Beutl.Converters;

internal sealed class VectorJsonConverter : StringJsonConverter<Vector>
{
    protected override string TypeName => "Vector";
    protected override Vector Parse(string s) => Vector.Parse(s);
}
