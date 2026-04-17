using Beutl.Graphics;

namespace Beutl.Converters;

internal sealed class MatrixJsonConverter : StringJsonConverter<Matrix>
{
    protected override string TypeName => "Matrix";
    protected override Matrix Parse(string s) => Matrix.Parse(s);
}
