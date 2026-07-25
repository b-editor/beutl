namespace Beutl.UnitTests.Core;

// The Try* pair exists so the caller owns the fallback: the product default is an app-layer theme id
// that this class must never hand out, and ViewConfig is what substitutes it for a value naming no
// theme. ViewConfigThemeMigrationTests covers that substitution; here only the null contract.
[TestFixture]
public class BuiltinThemeIdsTests
{
    [TestCase(0, BuiltinThemeIds.Light)]
    [TestCase(2, BuiltinThemeIds.HighContrast)]
    [TestCase(3, BuiltinThemeIds.System)]
    public void TryFromLegacyEnum_MapsTheMembersThatNameATheme(int value, string expected)
    {
        Assert.That(BuiltinThemeIds.TryFromLegacyEnum(value), Is.EqualTo(expected));
    }

    // 1 (Dark) was the pre-2.0 default, so it marks a user who never chose a theme; 4+ was never a
    // member. Both name no theme, and the caller decides where they land.
    [TestCase(1)]
    [TestCase(4)]
    [TestCase(-1)]
    public void TryFromLegacyEnum_ReturnsNull_WhenTheValueNamesNoTheme(int value)
    {
        Assert.That(BuiltinThemeIds.TryFromLegacyEnum(value), Is.Null);
    }

    [TestCase("Dark", BuiltinThemeIds.Dark)]
    [TestCase("  system  ", BuiltinThemeIds.System)]
    [TestCase("2", BuiltinThemeIds.HighContrast)]
    [TestCase("plugin.solarized", "plugin.solarized")]
    [TestCase("2026", "2026")]
    public void TryNormalize_CanonicalizesBuiltinsAndKeepsCustomIds(string raw, string expected)
    {
        Assert.That(BuiltinThemeIds.TryNormalize(raw), Is.EqualTo(expected));
    }

    [TestCase((string?)null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("1")]
    public void TryNormalize_ReturnsNull_WhenTheValueNamesNoTheme(string? raw)
    {
        Assert.That(BuiltinThemeIds.TryNormalize(raw), Is.Null);
    }

    // ThemeRegistry rejects these: settings would rewrite the id on the next load, so an extension
    // registering one would silently lose the user's selection.
    [TestCase("dark")]
    [TestCase("HighContrast")]
    [TestCase("0")]
    [TestCase("1")]
    [TestCase("")]
    public void IsReserved_CoversEveryIdSettingsWouldRewrite(string id)
    {
        Assert.That(BuiltinThemeIds.IsReserved(id), Is.True);
    }

    [TestCase("beutl.dark.border")]
    [TestCase("darker")]
    [TestCase("2026")]
    public void IsReserved_LeavesIdsSettingsHandsBackUnchanged(string id)
    {
        Assert.That(BuiltinThemeIds.IsReserved(id), Is.False);
    }
}
