using Beutl.Api;
using Beutl.Api.Clients;
using Beutl.Api.Services;

namespace Beutl.UnitTests.Api;

[TestFixture]
[NonParallelizable]
public sealed class PackageInstallerVerificationTests
{
    // Operations on different packages run at the same time, as when a store's packages are installed one after another.
    [Test]
    public async Task Concurrent_preparation_keeps_the_context_of_each_package()
    {
        const int Count = 5000;
        using var http = new HttpClient();
        await using var app = new BeutlApiApplication(http, new ExtensionProvider());
        await using var installer = new PackageInstaller(http, false, new InstalledPackageRepository(), app);
        var contexts = new PackageInstallContext[Count];

        Parallel.For(0, Count, i => contexts[i] = installer.PrepareForInstall($"Concurrent.{i}", "1.0.0", force: true));

        for (int i = 0; i < Count; i++)
        {
            Assert.That(installer.PrepareForInstall($"Concurrent.{i}", "1.0.0", force: true), Is.SameAs(contexts[i]));
        }
    }

    [Test]
    public async Task Concurrent_preparation_of_one_package_gives_one_context()
    {
        using var http = new HttpClient();
        await using var app = new BeutlApiApplication(http, new ExtensionProvider());
        await using var installer = new PackageInstaller(http, false, new InstalledPackageRepository(), app);
        var contexts = new PackageInstallContext[64];

        Parallel.For(0, contexts.Length, i => contexts[i] = installer.PrepareForInstall("Concurrent", "1.0.0", force: true));

        Assert.That(contexts, Is.All.SameAs(contexts[0]));
    }

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
            Id = "hash-test",
            Name = name,
            ContentType = "application/octet-stream",
            DownloadUrl = "https://example.com/package.nupkg",
            Size = 1,
            Sha256 = new string('0', 64),
        };
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => installer.VerifyPackageFile(context));
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
