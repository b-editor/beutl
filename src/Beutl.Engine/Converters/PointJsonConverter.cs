using Beutl.Graphics;

namespace Beutl.Converters;

internal sealed class PointJsonConverter : StringJsonConverter<Point>
{
    protected override string TypeName => "Point";
    protected override Point Parse(string s) => Point.Parse(s);
}
