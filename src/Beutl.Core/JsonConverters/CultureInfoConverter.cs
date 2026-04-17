using System.Globalization;

namespace Beutl.JsonConverters;

internal class CultureInfoConverter : StringJsonConverter<CultureInfo>
{
    protected override string TypeName => "CultureInfo";
    protected override CultureInfo Parse(string s) => CultureInfo.GetCultureInfo(s);
    protected override string Format(CultureInfo value) => value.Name;
}
