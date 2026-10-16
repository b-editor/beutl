using Beutl.Api.Services;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace Beutl.UnitTests.Api;

[TestFixture]
public sealed class PackageApiCheckCacheTests
{
    private static readonly MetadataReference s_coreLibrary = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);

    private string _root = null!;
    private string _store = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Directory.CreateTempSubdirectory("beutl-api-check-cache-").FullName;
        _store = Path.Combine(_root, "store", "packageApiChecks.json");
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_root, recursive: true);

    [Test]
    public void A_saved_pass_is_found_again_and_other_keys_and_packages_are_not()
    {
        new PackageApiCheckCache(_store).SavePass("Package", "key");
        var cache = new PackageApiCheckCache(_store);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(cache.HasPassed("package", "key"), Is.True);
            Assert.That(cache.HasPassed("Package", "other key"), Is.False);
            Assert.That(cache.HasPassed("Other", "key"), Is.False);
        }
    }

    [Test]
    public void The_key_changes_with_the_versions_the_package_assemblies_and_the_referenced_host_assemblies()
    {
        const string Source = "public class Uses { public HostLib.Widget? Widget; }";
        string host = Compile("host", "HostLib", "namespace HostLib; public class Widget { }");
        string rebuiltHost = Compile("rebuilt-host", "HostLib", "namespace HostLib; public class Widget { public int Size; }");
        string unreferenced = Compile("other", "OtherLib", "namespace OtherLib; public class Thing { }");
        string plugin = Compile("plugin", "Plugin", Source, host);
        string rebuiltPlugin = Compile("rebuilt-plugin", "Plugin", Source + " public class Added { }", host);
        string key = PackageApiCheckCache.CreateKey("1.0.0", [plugin], Host(host), "2.0.0");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(PackageApiCheckCache.CreateKey("1.0.0", [plugin], Host(host, unreferenced), "2.0.0"), Is.EqualTo(key));
            Assert.That(PackageApiCheckCache.CreateKey("1.0.1", [plugin], Host(host), "2.0.0"), Is.Not.EqualTo(key));
            Assert.That(PackageApiCheckCache.CreateKey("1.0.0", [plugin], Host(host), "2.0.1"), Is.Not.EqualTo(key));
            Assert.That(PackageApiCheckCache.CreateKey("1.0.0", [rebuiltPlugin], Host(host), "2.0.0"), Is.Not.EqualTo(key));
            Assert.That(PackageApiCheckCache.CreateKey("1.0.0", [plugin], Host(rebuiltHost), "2.0.0"), Is.Not.EqualTo(key));
        }
    }

    [TestCase("{ not json")]
    [TestCase("[\"Package\"]")]
    [TestCase("{ \"Package\": 1 }")]
    public void A_corrupt_store_reads_as_no_passes_and_the_next_pass_replaces_it(string contents)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_store)!);
        File.WriteAllText(_store, contents);
        var cache = new PackageApiCheckCache(_store);

        Assert.That(cache.HasPassed("Package", "key"), Is.False);
        cache.SavePass("Package", "key");
        Assert.That(new PackageApiCheckCache(_store).HasPassed("Package", "key"), Is.True);
    }

    [Test]
    public void A_store_that_cannot_be_written_keeps_the_pass_for_this_session_only()
    {
        Directory.CreateDirectory(_store);
        var cache = new PackageApiCheckCache(_store);

        cache.SavePass("Package", "key");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(cache.HasPassed("Package", "key"), Is.True);
            Assert.That(new PackageApiCheckCache(_store).HasPassed("Package", "key"), Is.False);
        }
    }

    // The host provides the framework and the given assemblies.
    private static Dictionary<string, string?> Host(params string[] assemblies)
    {
        var host = new Dictionary<string, string?> { ["System.Private.CoreLib"] = typeof(object).Assembly.Location };
        foreach (string path in assemblies)
        {
            host[Path.GetFileNameWithoutExtension(path)] = path;
        }

        return host;
    }

    private string Compile(string directory, string name, string source, params string[] references)
    {
        string path = Path.Combine(Directory.CreateDirectory(Path.Combine(_root, directory)).FullName, $"{name}.dll");
        EmitResult result = CSharpCompilation.Create(
                name,
                [CSharpSyntaxTree.ParseText(source)],
                [s_coreLibrary, .. references.Select(reference => MetadataReference.CreateFromFile(reference))],
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable))
            .Emit(path);
        Assert.That(result.Success, Is.True, () => string.Join(Environment.NewLine, result.Diagnostics));
        return path;
    }
}
