using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text;
using System.Text.RegularExpressions;
using Beutl.Language;

namespace Beutl.UnitTests.Language;

[TestFixture]
public partial class LocalizedResourceParityTests
{
    private static IEnumerable<TestCaseData> ResourceCases()
    {
        string[] families = [nameof(Strings), nameof(SettingsStrings), nameof(ExtensionsStrings)];
        foreach (string family in families)
        {
            foreach (CultureInfo culture in LocalizeService.Instance.SupportedCultures())
            {
                yield return new TestCaseData(family, culture.Name);
            }
        }
    }

    [TestCaseSource(nameof(ResourceCases))]
    public void Supported_cultures_have_matching_keys_and_placeholders(string family, string cultureName)
    {
        var manager = new ResourceManager($"Beutl.Language.{family}", typeof(Strings).Assembly);
        try
        {
            ResourceSet neutral = manager.GetResourceSet(CultureInfo.InvariantCulture, true, false)!;
            ResourceSet localized = GetLocalizedResourceSet(manager, cultureName);
            Dictionary<string, string> expected = ReadStrings(neutral);
            Dictionary<string, string> actual = ReadStrings(localized);

            Assert.Multiple(() =>
            {
                Assert.That(actual.Keys, Is.EquivalentTo(expected.Keys), $"{family}/{cultureName}: resource keys");

                foreach ((string key, string value) in expected)
                {
                    string context = $"{family}/{cultureName}/{key}";
                    if (!actual.TryGetValue(key, out string? translation))
                    {
                        Assert.Fail($"{context}: missing localized entry");
                        continue;
                    }

                    Assert.That(translation, Is.Not.Empty, context);
                    FormatItem[] expectedItems = GetPlaceholderSignature(value);
                    FormatItem[] actualItems = GetPlaceholderSignature(translation);
                    Assert.That(actualItems, Is.EquivalentTo(expectedItems), $"{context}: placeholder signature");

                    if (expectedItems.Length > 0 || actualItems.Length > 0)
                    {
                        Assert.That(() => CompositeFormat.Parse(value), Throws.Nothing, $"{context}: neutral format");
                        Assert.That(() => CompositeFormat.Parse(translation), Throws.Nothing, $"{context}: localized format");
                    }
                }
            });
        }
        finally
        {
            manager.ReleaseAllResources();
        }
    }

    private static ResourceSet GetLocalizedResourceSet(ResourceManager manager, string cultureName)
    {
        CultureInfo culture = CultureInfo.GetCultureInfo(cultureName);
        if (culture.TwoLetterISOLanguageName == "en")
        {
            return manager.GetResourceSet(CultureInfo.InvariantCulture, true, false)!;
        }

        // Allow a language satellite such as ja for ja-JP, but never fall back to English.
        while (!culture.Equals(CultureInfo.InvariantCulture))
        {
            ResourceSet? resources = manager.GetResourceSet(culture, true, false);
            if (resources is not null)
            {
                return resources;
            }

            culture = culture.Parent;
        }

        Assert.Fail($"{manager.BaseName}/{cultureName}: no localized resource set");
        return null!;
    }

    private static Dictionary<string, string> ReadStrings(ResourceSet resources)
    {
        return resources.Cast<DictionaryEntry>()
            .ToDictionary(entry => (string)entry.Key, entry => (string)entry.Value!);
    }

    private static FormatItem[] GetPlaceholderSignature(string value)
    {
        return PlaceholderRegex().Matches(value)
            .Where(match => match.Groups["index"].Success)
            .Select(match => new FormatItem(
                int.Parse(match.Groups["index"].Value, CultureInfo.InvariantCulture),
                match.Groups["alignment"].Success
                    ? int.Parse(match.Groups["alignment"].Value, CultureInfo.InvariantCulture)
                    : 0,
                match.Groups["format"].Success ? match.Groups["format"].Value : null))
            .ToArray();
    }

    [GeneratedRegex(@"\{\{|\}\}|\{(?<index>[0-9]+) *(?:, *(?<alignment>-?[0-9]+) *)?(?::(?<format>[^{}]*))?\}")]
    private static partial Regex PlaceholderRegex();

    private readonly record struct FormatItem(int Index, int Alignment, string? Format);
}
