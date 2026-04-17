using Beutl.Graphics;

namespace Beutl.Converters;

internal sealed class RectJsonConverter : StringJsonConverter<Rect>
{
    protected override string TypeName => "Rect";
    protected override Rect Parse(string s) => Rect.Parse(s);
}
