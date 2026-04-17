using Beutl.Media;

namespace Beutl.Converters;

internal sealed class FontFamilyJsonConverter : StringJsonConverter<FontFamily>
{
    protected override string TypeName => "FontFamily";
    protected override FontFamily Parse(string s) => new FontFamily(s);
    protected override string Format(FontFamily value) => value.Name;
}
