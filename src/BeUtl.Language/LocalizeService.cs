using System.Globalization;

namespace BeUtl.Language;

public sealed class LocalizeService
{
    public static readonly LocalizeService Instance = new();
    private readonly string[] _supported =
    {
        "en-US",
        "ja-JP",
    };

    public bool IsSupportedCulture(CultureInfo ci)
    {
        return _supported.Contains(ci.Name);
    }

    public Uri GetUri(CultureInfo ci)
    {
        if (!IsSupportedCulture(ci)) throw new InvalidOperationException();
        return new Uri($"avares://BeUtl.Language/{ci.Name}/CommonResources.axaml");
    }

    public IEnumerable<CultureInfo> SupportedCultures()
    {
        return _supported.Select(n => CultureInfo.GetCultureInfo(n));
    }
}
