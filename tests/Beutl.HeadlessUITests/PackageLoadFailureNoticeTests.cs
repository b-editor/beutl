using Beutl.Api.Services;
using Beutl.Language;
using Beutl.Services;
using Beutl.Services.StartupTasks;

namespace Beutl.HeadlessUITests;

public class PackageLoadFailureNoticeTests
{
    [Test]
    public void A_package_built_for_another_Beutl_version_is_named_with_the_APIs_it_misses()
    {
        var package = new LocalPackage { Name = "Old.Extension", DisplayName = "Old extension", Version = "1.0.0", TargetVersion = "2.0.0-preview.8" };
        var mismatch = new PackageApiMismatchException(package,
        [
            new(PackageApiProblemKind.MissingType, "Beutl.Engine", "Beutl.Audio.Effects.LegacyEffect"),
            new(PackageApiProblemKind.MissingType, "Beutl.Engine", "Beutl.Engine.Gone"),
        ]);
        var broken = new LocalPackage { Name = "Broken" };
        using var capture = new NotificationCapture();

        AfterLoadingExtensionsTask.ShowFailures([(package, mismatch), (broken, new InvalidOperationException("boom"))]);

        Notification[] shown = [.. capture.All];
        using (Assert.EnterMultipleScope())
        {
            Assert.That(shown, Has.Length.EqualTo(2));
            Assert.That(shown[0].Title, Is.EqualTo(string.Format(ExtensionsStrings.PackageNotLoaded, "Old extension")));
            Assert.That(shown[0].Message, Is.EqualTo(string.Format(
                ExtensionsStrings.PackageUsesMissingApisForVersion,
                "2.0.0-preview.8",
                2,
                BeutlApplication.Version,
                "Beutl.Audio.Effects.LegacyEffect, Beutl.Engine.Gone")));
            Assert.That(shown[1].Message, Is.EqualTo(string.Format(MessageStrings.FailedToLoadNPackages, 1)));
        }
    }
}
