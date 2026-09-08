using System.Globalization;

namespace Beutl.Language;

public sealed class LocalizeService
{
    public static readonly LocalizeService Instance = new();

    private static readonly string[] s_supported =
    [
        "en-US",
        "ja-JP",
        "zh-CN",
        "ko-KR",
        "es",
    ];

    public bool IsSupportedCulture(CultureInfo ci)
    {
        return ResolveSupportedCulture(ci) is not null;
    }

    // The list mixes specific ("ja-JP") and neutral ("es") entries, so a specific culture such as
    // es-MX can only match through its parent.
    public CultureInfo? ResolveSupportedCulture(CultureInfo ci)
    {
        foreach (string name in s_supported)
        {
            if (name == ci.Name || name == ci.Parent.Name)
                return CultureInfo.GetCultureInfo(name);
        }

        return null;
    }

    public IEnumerable<CultureInfo> SupportedCultures()
    {
        return s_supported.Select(CultureInfo.GetCultureInfo);
    }
}
