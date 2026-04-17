using Beutl.Graphics;

namespace Beutl.Converters;

internal sealed class SizeJsonConverter : StringJsonConverter<Size>
{
    protected override string TypeName => "Size";
    protected override Size Parse(string s) => Size.Parse(s);
}
