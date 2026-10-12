using System.Reflection;

namespace Beutl.UnitTests.Core;

public class BeutlApplicationVersionTests
{
    // The test runner is the entry assembly here, so a version read from it would be the fallback, and
    // every file that records a 2.x minimum would look too new.
    [Test]
    public void The_version_is_the_one_Beutl_Core_was_built_as()
    {
        string? built = typeof(BeutlApplication).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(attribute => attribute.Key == "NuGetVersion")
            ?.Value;

        Assert.Multiple(() =>
        {
            Assert.That(built, Is.Not.Null.And.Not.Empty);
            Assert.That(BeutlApplication.Version, Is.EqualTo(built));
        });
    }
}
