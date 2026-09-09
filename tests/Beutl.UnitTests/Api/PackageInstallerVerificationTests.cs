using Beutl.Api;
using Beutl.Api.Clients;
using Beutl.Api.Services;

namespace Beutl.UnitTests.Api;

[TestFixture]
[NonParallelizable]
public sealed class PackageInstallerVerificationTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task RejectedHash_RemovesTheDownloadFromTheLocalPackageSource(bool empty)
    {
        using var http = new HttpClient();
        await using var app = new BeutlApiApplication(http, new ExtensionProvider());
        await using var installer = new PackageInstaller(http, false, new InstalledPackageRepository(), app);
        string name = "Beutl.Package.HashTest." + Guid.NewGuid().ToString("N");
        string path = Helper.GetNupkgFilePath(name, "1.0.0");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, empty ? "" : "tampered payload");
        PackageInstallContext context = installer.PrepareForInstall(name, "1.0.0", force: true);
        context.NuGetPackageFile = path;
        context.Asset = new FileResponse
        {
                Id = "hash-test", Name = name, ContentType = "application/octet-stream",
                DownloadUrl = "https://example.com/package.nupkg", Size = 1, Sha256 = new string('0', 64),
        };
        try
        {
            Assert.ThrowsAsync<InvalidDataException>(() => installer.VerifyPackageFile(context));
            Assert.That(context.HashVerified, Is.False);
            Assert.That(installer.PrepareForInstall(name, "1.0.0", force: true), Is.Not.SameAs(context),
                "A later attempt must not retain the rejected context and skip its download phase.");
            Assert.That(File.Exists(path), Is.False, "A rejected archive must not remain available for local installation.");
        }
        finally
        {
            File.Delete(path);
        }
    }
}
