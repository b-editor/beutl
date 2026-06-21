using Beutl.Api;
using Beutl.Api.Services;

namespace Beutl.UnitTests.Api;

[TestFixture]
public sealed class BeutlApiApplicationTests
{
    [Test]
    public void Constructor_RegistersProvidedExtensionProvider()
    {
        using var httpClient = new HttpClient();
        var extensionProvider = new ExtensionProvider();
        var app = new BeutlApiApplication(httpClient, extensionProvider);

        ExtensionProvider registeredProvider = app.GetResource<ExtensionProvider>();
        PackageManager packageManager = app.GetResource<PackageManager>();

        Assert.That(registeredProvider, Is.SameAs(extensionProvider));
        Assert.That(packageManager.ExtensionProvider, Is.SameAs(extensionProvider));
    }
}
