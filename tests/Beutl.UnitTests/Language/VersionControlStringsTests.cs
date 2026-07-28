using System.Globalization;
using System.Resources;
using Beutl.Language;

namespace Beutl.UnitTests.Language;

[TestFixture]
public class VersionControlStringsTests
{
    private static readonly string[] s_requiredKeys =
    [
        "VersionControl",
        "VersionControl_Enable",
        "VersionControl_TrackHistory",
        "VersionControl_Commit",
        "VersionControl_Restore",
        "VersionControl_SnapshotSave",
        "VersionControl_GitNotInstalled",
        "VersionControl_ConflictGuidance",
        "VersionControl_RestoreConfirmation",
    ];

    [TestCase("en-US")]
    [TestCase("ja-JP")]
    public void Required_version_control_strings_are_localized(string cultureName)
    {
        var resourceManager = new ResourceManager("Beutl.Language.Strings", typeof(Strings).Assembly);
        CultureInfo culture = CultureInfo.GetCultureInfo(cultureName);

        Assert.Multiple(() =>
        {
            foreach (string key in s_requiredKeys)
            {
                Assert.That(resourceManager.GetString(key, culture), Is.Not.Null.And.Not.Empty, key);
            }
        });
    }
}
