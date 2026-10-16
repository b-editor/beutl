using Beutl.Api.Services;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace Beutl.UnitTests.Api;

[TestFixture]
[NonParallelizable]
public sealed class PackageCompatibilityTests
{
    private static readonly MetadataReference s_coreLibrary = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);

    private readonly List<string> _directories = [];
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Directory.CreateTempSubdirectory("beutl-package-compatibility-").FullName;
        _directories.Add(_root);
    }

    [TearDown]
    public void TearDown()
    {
        foreach (string directory in _directories.Where(Directory.Exists))
        {
            Directory.Delete(directory, recursive: true);
        }

        _directories.Clear();
    }

    [Test]
    public void A_sideloaded_package_that_uses_an_API_the_host_lacks_is_not_loaded()
    {
        // Built against a Beutl.Extensibility that has a type the host's lacks.
        string extensibility = Compile(_root, "Beutl.Extensibility", "namespace Beutl.Extensibility; public class NotInHost { }");
        string directory = Path.Combine(Helper.SideLoadsPath, "MismatchedPlugin");
        _directories.Add(directory);
        Compile(directory, "MismatchedPlugin", "public class Uses { public System.Type Type = typeof(Beutl.Extensibility.NotInHost); }", extensibility);
        var manager = new PackageManager(
            new InstalledPackageRepository(),
            new ExtensionProvider(),
            new ContextCommandManager(new ContextCommandSettingsStore()),
            apiApplication: null!);
        var package = new LocalPackage { Name = "MismatchedPlugin", InstalledPath = directory, SideLoad = true };

        PackageApiMismatchException exception = Assert.Throws<PackageApiMismatchException>(() => manager.Load(package))!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception.Problems, Is.EqualTo(new PackageApiProblem[]
            {
                new(PackageApiProblemKind.MissingType, "Beutl.Extensibility", "Beutl.Extensibility.NotInHost"),
            }));
            Assert.That(exception.Message, Does.Contain("Beutl.Extensibility.NotInHost"));
            Assert.That(manager.LoadedPackage, Does.Not.Contain(package));
        }
    }

    [Test]
    public void A_package_built_for_this_Beutl_version_is_not_checked_and_one_that_passes_is_saved()
    {
        string host = Compile(Path.Combine(_root, "host"), "HostLib", "namespace HostLib; public class Kept { }");
        string plugin = Compile(Path.Combine(_root, "plugin"), "Plugin", """
            public class Uses
            {
                public System.Type Kept = typeof(HostLib.Kept);
                public System.Type Gone = typeof(HostLib.Gone);
            }
            """, Compile(Path.Combine(_root, "old-host"), "HostLib", "namespace HostLib; public class Kept { } public class Gone { }"));
        string passing = Compile(Path.Combine(_root, "passing"), "Plugin", "public class Uses { public System.Type Kept = typeof(HostLib.Kept); }", host);
        var hostAssemblies = new Dictionary<string, string?> { ["System.Private.CoreLib"] = typeof(object).Assembly.Location, ["HostLib"] = host };
        var cache = new PackageApiCheckCache(Path.Combine(_root, "store", "packageApiChecks.json"));
        var compatibility = new PackageCompatibility(cache);
        var current = new LocalPackage { Name = "Current", Version = "1.0.0", TargetVersion = BeutlApplication.Version };
        var older = new LocalPackage { Name = "Older", Version = "1.0.0", TargetVersion = "1.0.0" };

        using (Assert.EnterMultipleScope())
        {
            Assert.That(() => compatibility.EnsureCompatible(current, [plugin], () => hostAssemblies), Throws.Nothing);
            Assert.That(() => compatibility.EnsureCompatible(older, [plugin], () => hostAssemblies), Throws.TypeOf<PackageApiMismatchException>());
            Assert.That(() => compatibility.EnsureCompatible(older, [passing], () => hostAssemblies), Throws.Nothing);
            Assert.That(cache.HasPassed("Older", PackageApiCheckCache.CreateKey("1.0.0", [passing], hostAssemblies, BeutlApplication.Version)), Is.True);
        }
    }

    [Test]
    public void The_host_assemblies_leave_out_the_ones_the_package_brings()
    {
        Dictionary<string, string?> host = PackageCompatibility.HostAssemblies(["beutl.extensibility"]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(host, Does.ContainKey("System.Private.CoreLib").And.ContainKey("Beutl.Engine"));
            Assert.That(host, Does.Not.ContainKey("Beutl.Extensibility"));
        }
    }

    private static string Compile(string directory, string name, string source, params string[] references)
    {
        string path = Path.Combine(Directory.CreateDirectory(directory).FullName, $"{name}.dll");
        EmitResult result = CSharpCompilation.Create(
                name,
                [CSharpSyntaxTree.ParseText(source)],
                [s_coreLibrary, .. references.Select(reference => MetadataReference.CreateFromFile(reference))],
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .Emit(path);
        Assert.That(result.Success, Is.True, () => string.Join(Environment.NewLine, result.Diagnostics));
        return path;
    }
}
