using System.Text;
using Beutl.Api.Services;
using NuGet.Packaging;

namespace Beutl.UnitTests.Api;

[TestFixture]
public class LocalPackageTests
{
    [TestCase("Beutl.Sdk")]
    [TestCase("Beutl.Extensibility")]
    [TestCase("Beutl.Extensibility.Abstractions")]
    public void Constructor_ReadsTargetVersionFromBeutlDependency(string dependencyId)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(CreateNuspec(dependencyId)));
        var package = new LocalPackage(new NuspecReader(stream));

        Assert.That(package.TargetVersion, Does.Contain("2.99.99"));
    }

    [Test]
    public void Constructor_PrefersHigherPrecedenceBeutlDependency()
    {
        // The fallback chain is Beutl.Sdk > Beutl.Extensibility > Beutl.Extensibility.Abstractions,
        // so a package declaring several Beutl dependencies must report the highest-precedence one.
        LocalPackage extensibilityOverAbstractions = CreatePackage(
            ("Beutl.Extensibility", "[1.1.1, )"),
            ("Beutl.Extensibility.Abstractions", "[2.2.2, )"));
        LocalPackage sdkOverTheRest = CreatePackage(
            ("Beutl.Extensibility", "[1.1.1, )"),
            ("Beutl.Sdk", "[3.3.3, )"),
            ("Beutl.Extensibility.Abstractions", "[2.2.2, )"));

        Assert.Multiple(() =>
        {
            Assert.That(extensibilityOverAbstractions.TargetVersion, Does.Contain("1.1.1"));
            Assert.That(extensibilityOverAbstractions.TargetVersion, Does.Not.Contain("2.2.2"));
            Assert.That(sdkOverTheRest.TargetVersion, Does.Contain("3.3.3"));
            Assert.That(sdkOverTheRest.TargetVersion, Does.Not.Contain("1.1.1"));
            Assert.That(sdkOverTheRest.TargetVersion, Does.Not.Contain("2.2.2"));
        });
    }

    private static LocalPackage CreatePackage(params (string Id, string Version)[] dependencies)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(CreateNuspec(dependencies)));
        return new LocalPackage(new NuspecReader(stream));
    }

    private static string CreateNuspec(string dependencyId) =>
        CreateNuspec((dependencyId, "[2.99.99, )"));

    private static string CreateNuspec(params (string Id, string Version)[] dependencies)
    {
        string depElements = string.Join(
            "\n",
            dependencies.Select(d => $"""        <dependency id="{d.Id}" version="{d.Version}" />"""));

        return $$"""
            <?xml version="1.0"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
              <metadata>
                <id>Sample.Extension</id>
                <version>1.0.0</version>
                <authors>b-editor</authors>
                <description>Sample extension.</description>
                <dependencies>
                  <group targetFramework="net10.0">
            {{depElements}}
                  </group>
                </dependencies>
              </metadata>
            </package>
            """;
    }
}
