using System.Globalization;

namespace Beutl.Language;

public sealed class LocalizeService
{
    public static readonly LocalizeService Instance = new();
    private readonly string[] _supported =
    [
        "en-US",
        "ja-JP",
    ];

    public bool IsSupportedCulture(CultureInfo ci)
    {
        return _supported.Contains(ci.Name);
    }

    public IEnumerable<CultureInfo> SupportedCultures()
    {
        return _supported.Select(CultureInfo.GetCultureInfo);
    }
}
