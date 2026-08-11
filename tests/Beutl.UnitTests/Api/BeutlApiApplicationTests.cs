using System.Net;
using System.Text;
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

    [Test]
    public void Constructor_NullHttpClient_Throws()
    {
        var extensionProvider = new ExtensionProvider();

        Assert.Throws<ArgumentNullException>(() => _ = new BeutlApiApplication(null!, extensionProvider));
    }

    [Test]
    public void Constructor_NullExtensionProvider_Throws()
    {
        using var httpClient = new HttpClient();

        Assert.Throws<ArgumentNullException>(() => _ = new BeutlApiApplication(httpClient, null!));
    }

    [Test]
    public async Task CheckForUpdatesAsync_WithFlatpakMetadata_SendsZipType()
    {
        // The server's /api/v3/app/updates endpoint only accepts zip/debian/installer/app.
        // Flatpak bundles are built from the standalone zip, so report them as zip.
        // (s_metadata is a per-process static cache, so this must be the only test exercising the metadata path.)
        string metadataPath = Path.Combine(AppContext.BaseDirectory, "asset_metadata.json");
        File.WriteAllText(metadataPath, """
            {
              "id": "test-id",
              "os": "linux",
              "arch": "x64",
              "version": "2.0.0-preview.6",
              "standalone": "true",
              "type": "flatpak"
            }
            """);
        try
        {
            var handler = new CapturingHandler();
            using var httpClient = new HttpClient(handler);
            var app = new BeutlApiApplication(httpClient, new ExtensionProvider());

            var (v1, v3) = await app.CheckForUpdatesAsync("2.0.0-preview.6");

            Assert.That(handler.LastRequestUri, Is.Not.Null);
            Assert.That(handler.LastRequestUri!.Query, Does.Contain("type=zip"));
            Assert.That(handler.LastRequestUri!.Query, Does.Not.Contain("type=flatpak"));
            Assert.That(v3, Is.Not.Null);
            Assert.That(v1, Is.Null);
        }
        finally
        {
            File.Delete(metadataPath);
        }
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {"latestVersion":"2.0.0-preview.7","url":"https://github.com/b-editor/beutl/releases/tag/v2.0.0-preview.7","downloadUrl":null,"isLatest":false,"mustLatest":false}
                    """,
                    Encoding.UTF8,
                    "application/json")
            };
            return Task.FromResult(response);
        }
    }
}
