using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;

namespace Beutl.UnitTests.Build;

[TestFixture]
public class SkiaNativeAssetSelectionTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task RepositoryBuildsUseCommittedBinariesWithoutAnOverride(bool ridSpecific)
    {
        using var fixture = new AssetFixture("project", ridSpecific, "4.152.1", useBundledAssets: true);
        (int exitCode, string output) = await fixture.Resolve();
        Assert.That(exitCode, Is.Zero, output);
        using JsonDocument result = JsonDocument.Parse(output);
        string selected = result.RootElement.GetProperty("Items").GetProperty(fixture.ItemName)[0]
            .GetProperty("Identity").GetString()!;
        Assert.That(Path.GetFullPath(selected), Is.EqualTo(Path.GetFullPath(fixture.PatchedLibrary)));
        Assert.That(File.Exists(selected), Is.True);
    }

    [TestCase("standalone", false)]
    [TestCase("standalone", true)]
    [TestCase("engine", false)]
    [TestCase("engine", true)]
    [TestCase("project", false)]
    [TestCase("transitive", false)]
    [TestCase("package", false)]
    [TestCase("package", true)]
    [TestCase("runtime", true)]
    public async Task SelectsPatchedAssetsOnlyForEngineConsumers(string referenceKind, bool ridSpecific)
    {
        bool usesEngine = referenceKind != "standalone";
        using var fixture = new AssetFixture(referenceKind, ridSpecific, usesEngine ? "4.152.1" : "3.119.4");
        (int exitCode, string output) = await fixture.Resolve();
        Assert.That(exitCode, Is.Zero, output);
        using JsonDocument result = JsonDocument.Parse(output);
        JsonElement items = result.RootElement.GetProperty("Items");
        Assert.That(items.GetProperty(fixture.ItemName)[0].GetProperty("Identity").GetString(),
            Is.EqualTo(usesEngine ? fixture.PatchedLibrary : fixture.StockLibrary));
        JsonElement notices = items.GetProperty("ContentWithTargetPath");
        Assert.That(notices.GetArrayLength(), Is.EqualTo(usesEngine ? 2 : 0));
        if (usesEngine)
            Assert.That(notices[0].GetProperty("TargetPath").GetString(), Is.EqualTo("licenses/SkiaSharp/Skia.LICENSE"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task EngineConsumersStillRejectAnIncompatibleNativeVersion(bool ridSpecific)
    {
        using var fixture = new AssetFixture("package", ridSpecific, "3.119.4");
        (int exitCode, string output) = await fixture.Resolve();
        Assert.That(exitCode, Is.Not.Zero);
        Assert.That(output, Does.Contain("Update native/SkiaSharp/source.json"));
    }

    [Test]
    public async Task EngineConsumersStillRequireThePatchedBinary()
    {
        using var fixture = new AssetFixture("transitive", false, "4.152.1");
        File.Delete(fixture.PatchedLibrary);
        (int exitCode, string output) = await fixture.Resolve();
        Assert.That(exitCode, Is.Not.Zero);
        Assert.That(output, Does.Contain("requires its patched libSkiaSharp"));
    }

    private sealed class AssetFixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "beutl-skia-assets-" + Guid.NewGuid().ToString("N"));
        private readonly string _repository;
        private readonly string _project;

        public AssetFixture(string referenceKind, bool ridSpecific, string version, bool useBundledAssets = false)
        {
            DirectoryInfo? repository = new(AppContext.BaseDirectory);
            while (repository != null && !File.Exists(Path.Combine(repository.FullName, "native", "SkiaSharp", "Beutl.Engine.targets")))
                repository = repository.Parent;
            _repository = repository?.FullName ?? throw new InvalidOperationException("Beutl repository not found.");
            string nativeRoot = useBundledAssets
                ? Path.Combine(_repository, "src", "Beutl.Engine")
                : Path.Combine(_directory, "patched");
            const string assetPath = "runtimes/linux-x64/native/libSkiaSharp.so";
            StockLibrary = Path.Combine(_directory, "stock", "libSkiaSharp.so");
            PatchedLibrary = Path.Combine(nativeRoot, assetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(StockLibrary)!);
            File.WriteAllText(StockLibrary, "stock");
            if (!useBundledAssets)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PatchedLibrary)!);
                File.WriteAllText(PatchedLibrary, "patched");
                File.WriteAllText(Path.Combine(Path.GetDirectoryName(PatchedLibrary)!, "Skia.LICENSE"), "license");
                File.WriteAllText(Path.Combine(Path.GetDirectoryName(PatchedLibrary)!, "Skia.NOTICES"), "notices");
            }
            ItemName = ridSpecific ? "NativeCopyLocalItems" : "RuntimeTargetsCopyLocalItems";
            var references = new XElement("ItemGroup");
            string? referenceItem = referenceKind switch
            {
                "project" => "ProjectReference",
                "transitive" => "_TransitiveProjectReferences",
                "package" => "ResolvedCompileFileDefinitions",
                "runtime" => "RuntimeCopyLocalItems",
                _ => null,
            };
            if (referenceItem != null)
                references.Add(new XElement(referenceItem, new XAttribute("Include",
                    referenceKind is "project" or "transitive" ? "Beutl.Engine.csproj" : "Beutl.Engine.dll")));
            var project = new XElement("Project",
                useBundledAssets
                    ? new XElement("Import", new XAttribute("Project", Path.Combine(_repository, "Directory.Build.props")))
                    : null,
                new XElement("PropertyGroup",
                    new XElement("NETCoreSdkRuntimeIdentifier", "linux-x64"),
                    new XElement("RuntimeIdentifier", ridSpecific ? "linux-x64" : ""),
                    useBundledAssets ? null : new XElement("BeutlSkiaSharpNativeRoot", nativeRoot + Path.DirectorySeparatorChar)),
                new XElement("Target", new XAttribute("Name", "ResolvePackageAssets"),
                    references,
                    new XElement("ItemGroup", new XElement(ItemName, new XAttribute("Include", StockLibrary),
                        new XElement("NuGetPackageId", "SkiaSharp.NativeAssets.Linux"),
                        new XElement("NuGetPackageVersion", version),
                        new XElement("PathInPackage", assetPath),
                        new XElement("RuntimeIdentifier", "linux-x64")))),
                new XElement("Import", new XAttribute("Project",
                    Path.Combine(_repository, "native", "SkiaSharp", "Beutl.Engine.targets"))));
            _project = Path.Combine(_directory, referenceKind == "engine" ? "Beutl.Engine.proj" : "Consumer.proj");
            project.Save(_project);
        }

        public string ItemName { get; }
        public string StockLibrary { get; }
        public string PatchedLibrary { get; }

        public async Task<(int ExitCode, string Output)> Resolve()
        {
            var start = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = _repository,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (string argument in new[] { "msbuild", _project, "-nologo", "-t:ResolvePackageAssets",
                         $"-getItem:{ItemName},ContentWithTargetPath" })
                start.ArgumentList.Add(argument);
            using Process process = Process.Start(start)!;
            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
                return (process.ExitCode, await stdout + await stderr);
            }
            finally
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
        }

        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }
}
