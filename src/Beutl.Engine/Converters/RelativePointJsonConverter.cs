using Beutl.Graphics;

namespace Beutl.Converters;

internal sealed class RelativePointJsonConverter : StringJsonConverter<RelativePoint>
{
    protected override string TypeName => "RelativePoint";
    protected override RelativePoint Parse(string s) => RelativePoint.Parse(s);
}
