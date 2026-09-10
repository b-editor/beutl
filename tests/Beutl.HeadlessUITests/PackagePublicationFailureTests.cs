using System.Reflection;
using Avalonia.Headless.NUnit;
using Beutl.Api;
using Beutl.Api.Services;
using Beutl.Services;
using Beutl.ViewModels.ExtensionsPages;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public class PackagePublicationFailureTests
{
    [AvaloniaTest]
    [TestCase("data", true)]
    [TestCase("load", true)]
    [TestCase("register-extension", true)]
    [TestCase("data", false)]
    public async Task FailedActivation_RestoresPayloadAndPreviousRegistration(string mode, bool existing)
    {
        await TestReset.ResetShellAsync();
        string registrationFile = Path.Combine(Helper.AppRoot, "installedPackages.json");
        byte[]? originalRegistration = File.Exists(registrationFile) ? File.ReadAllBytes(registrationFile) : null;
        if (originalRegistration is null) File.WriteAllText(registrationFile, "[]");
        string backup = registrationFile + ".test-backup";
        string name = "PublicationFailure." + Guid.NewGuid().ToString("N");
        var oldId = new PackageIdentity(name, NuGetVersion.Parse("1.0.0"));
        var newId = new PackageIdentity(name, NuGetVersion.Parse("2.0.0"));
        LocalPackage old = CreatePackage(oldId, data: true);
        LocalPackage next = CreatePackage(newId, data: mode == "data", loadable: mode == "register-extension");
        using var http = new HttpClient();
        await using var app = new BeutlApiApplication(http, new ExtensionProvider());
        var installer = app.GetResource<PackageInstaller>();
        var repository = app.GetResource<InstalledPackageRepository>();
        var operation = new PackageOperationHandler(app, new EditorService(new ExtensionProvider()), new ProjectService());
        string materials = Path.Combine(BeutlEnvironment.GetMaterialsDirectoryPath(), name);
        string templates = Path.Combine(BeutlEnvironment.GetTemplatesDirectoryPath(), name);
        bool extensionRegistered = false;
        var manager = app.GetResource<PackageManager>();
        typeof(PackageManager).GetProperty("AfterExtensionRegistration", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(manager, (Action)(() => extensionRegistered = true));
        try
        {
            if (existing)
            {
                repository.UpgradePackages(oldId);
                installer.InstallDataPackage(old);
            }
            byte[]? oldMarker = existing ? File.ReadAllBytes(Path.Combine(materials, ".beutl-package-owner")) : null;
            if (mode != "load")
            {
                File.Move(registrationFile, backup);
                Directory.CreateDirectory(registrationFile);
            }
            var method = typeof(PackageOperationHandler).GetMethod("ActivateInstalledPackageAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Exception? failure = null;
            try { await ((Task)method.Invoke(operation, new object[] { newId, CancellationToken.None })!).WaitAsync(TimeSpan.FromSeconds(10)); }
            catch (Exception ex) { failure = ex; }
            Assert.That(failure, Is.Not.Null.And.Not.TypeOf<TimeoutException>());
            Assert.Multiple(() =>
            {
                if (existing)
                {
                    Assert.That(File.ReadAllText(Path.Combine(materials, "item.txt")), Is.EqualTo("1.0.0"));
                    Assert.That(File.ReadAllText(Path.Combine(templates, "item.txt")), Is.EqualTo("1.0.0"));
                    Assert.That(File.ReadAllBytes(Path.Combine(materials, ".beutl-package-owner")), Is.EqualTo(oldMarker));
                }
                else
                {
                    Assert.That(Directory.Exists(materials), Is.False);
                    Assert.That(Directory.Exists(templates), Is.False);
                }
                Assert.That(extensionRegistered, Is.EqualTo(mode == "register-extension"));
                Assert.That(manager.FindLoadedPackage(name), Is.Empty);
                Assert.That(repository.ExistsPackage(oldId), Is.EqualTo(existing));
                Assert.That(repository.ExistsPackage(newId), Is.False);
            });
        }
        finally
        {
            if (Directory.Exists(registrationFile)) Directory.Delete(registrationFile);
            if (File.Exists(backup)) File.Move(backup, registrationFile, true);
            if (originalRegistration is not null) File.WriteAllBytes(registrationFile, originalRegistration);
            else File.Delete(registrationFile);
            foreach (string directory in new[] { old.InstalledPath!, next.InstalledPath!, materials, templates })
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private static LocalPackage CreatePackage(PackageIdentity identity, bool data, bool loadable = false)
    {
        string version = identity.Version.ToString();
        string directory = Path.Combine(Helper.InstallPath, $"{identity.Id}.{version}");
        Directory.CreateDirectory(directory);
        string[] tags = data ? [PackageKinds.MaterialTag, PackageKinds.TemplateTag] : ["effects"];
        File.WriteAllText(Path.Combine(directory, $"{identity.Id}.{version}.nupkg"), "");
        string dependencies = loadable ? "<dependencies><group targetFramework=\"net10.0\" /></dependencies>" : "";
        if (loadable) Directory.CreateDirectory(Path.Combine(directory, "lib", Helper.GetFrameworkName().ToString()));
        File.WriteAllText(Path.Combine(directory, identity.Id + ".nuspec"),
            $"<package><metadata><id>{identity.Id}</id><version>{version}</version><authors>test</authors><description>test</description><tags>{string.Join(' ', tags)}</tags>{dependencies}</metadata></package>");
        if (data)
        {
            foreach (string kind in new[] { "materials", "templates" })
            {
                Directory.CreateDirectory(Path.Combine(directory, kind));
                File.WriteAllText(Path.Combine(directory, kind, "item.txt"), version);
            }
        }
        return new LocalPackage { Name = identity.Id, Version = version, Tags = [.. tags], InstalledPath = directory };
    }
}
