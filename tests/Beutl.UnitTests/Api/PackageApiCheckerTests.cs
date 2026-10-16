using System.Collections.Immutable;
using Beutl.Api.Services;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace Beutl.UnitTests.Api;

// The assemblies are compiled here: HostLib stands for a Beutl assembly in two versions, and Plugin for an extension
// built against one of them.
[TestFixture]
public sealed partial class PackageApiCheckerTests
{
    private const string HostV1 = """
        namespace HostLib;
        public class Widget
        {
            public class Part { }
            public class Gear { }
        }
        public struct Point { }
        public interface IHandler { }
        public class Gone { }
        public class MadeInternal { }
        public class Moved { }
        public class MovedWithoutForwarder { }
        """;

    // Drops Widget.Gear and Gone, hides MadeInternal, and moves Moved (forwarded) and MovedWithoutForwarder to Core.
    private const string HostV2 = """
        [assembly: System.Runtime.CompilerServices.TypeForwardedTo(typeof(HostLib.Moved))]
        namespace HostLib;
        public class Widget
        {
            public class Part { }
        }
        public struct Point { }
        public interface IHandler { }
        internal class MadeInternal { }
        public class Added { }
        """;

    private const string HostV2Core = """
        namespace HostLib;
        public class Moved { }
        public class MovedWithoutForwarder { }
        """;

    private const string PluginUsingEverything = """
        using HostLib;
        public class Handler : IHandler
        {
            public Widget.Part? Part;
            public Widget.Gear? Gear;
            public Point Point;
            public System.Type[] Types = [typeof(Gone), typeof(MadeInternal), typeof(Moved), typeof(MovedWithoutForwarder)];
        }
        """;

    private const string PluginUsingTheUnchangedSurface = """
        using HostLib;
        public class Handler : IHandler
        {
            public Widget.Part? Part;
            public Point Point;
            public System.Type Type = typeof(Moved);
        }
        """;

    // A valid strong-name public key: the one DynamicProxyGenAssembly2 is signed with.
    private const string PublicKey =
        "0024000004800000940000000602000000240000525341310004000001000100c547cac37abd99c8db225ef2f6c8a3602f3b3606cc9891605"
        + "d02baa56104f4cfc0734aa39b93bf7852f7d9266654753cc297e7d2edfe0bac1cdcf9f717241550e0a7b191195b7667bb4f64bcb8e21213"
        + "80fd1d9d46ad2d92d2d15605093924cceaf74c4861eff62abf69b9291ed0a340e113be11e6a7d3113e92484cf7045cc7";

    private static readonly MetadataReference s_coreLibrary = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);

    private string _root = null!;

    [SetUp]
    public void SetUp() => _root = Directory.CreateTempSubdirectory("beutl-api-check-").FullName;

    [TearDown]
    public void TearDown() => Directory.Delete(_root, recursive: true);

    [Test]
    public void Passes_a_package_checked_against_the_host_it_was_built_for()
    {
        string v1 = Compile("v1", "HostLib", "1.0.0.0", HostV1);
        string plugin = Compile("plugin", "Plugin", "1.0.0.0", PluginUsingEverything, v1);

        Assert.That(PackageApiChecker.Check([plugin], Host(v1)), Is.Empty);
    }

    [Test]
    public void Passes_a_package_built_for_an_older_host_when_the_types_it_uses_are_unchanged()
    {
        string plugin = Compile("plugin", "Plugin", "1.0.0.0", PluginUsingTheUnchangedSurface, Compile("v1", "HostLib", "1.0.0.0", HostV1));
        (string v2, string v2Core) = CompileV2();

        Assert.That(PackageApiChecker.Check([plugin], Host(v2, v2Core)), Is.Empty);
    }

    [Test]
    public void Reports_each_removed_or_hidden_type_that_a_package_built_for_an_older_host_uses()
    {
        string plugin = Compile("plugin", "Plugin", "1.0.0.0", PluginUsingEverything, Compile("v1", "HostLib", "1.0.0.0", HostV1));
        (string v2, string v2Core) = CompileV2();

        Assert.That(PackageApiChecker.Check([plugin], Host(v2, v2Core)), Is.EqualTo(new PackageApiProblem[]
        {
            new(PackageApiProblemKind.MissingType, "HostLib", "HostLib.Gone"),
            new(PackageApiProblemKind.MissingType, "HostLib", "HostLib.MadeInternal"),
            new(PackageApiProblemKind.MissingType, "HostLib", "HostLib.MovedWithoutForwarder"),
            new(PackageApiProblemKind.MissingType, "HostLib", "HostLib.Widget+Gear"),
        }));
    }

    [Test]
    public void Reports_what_a_package_built_for_a_newer_host_uses_and_the_host_lacks()
    {
        (string v2, string v2Core) = CompileV2();
        string plugin = Compile("plugin", "Plugin", "1.0.0.0", """
            public class Uses
            {
                public HostLib.Widget.Part? Part;
                public HostLib.Added? Added;
            }
            """, v2, v2Core);

        Assert.That(
            PackageApiChecker.Check([plugin], Host(Compile("v1", "HostLib", "1.0.0.0", HostV1))),
            Is.EqualTo(new PackageApiProblem[] { new(PackageApiProblemKind.MissingType, "HostLib", "HostLib.Added") }));
    }

    [Test]
    public void Lets_a_package_use_the_internal_types_that_the_host_grants_it_by_InternalsVisibleTo()
    {
        const string Source = "public class Uses { public System.Type Type = typeof(HostLib.MadeInternal); }";
        string v1 = Compile("v1", "HostLib", "1.0.0.0", HostV1);
        string plugin = Compile("plugin", "Plugin", "1.0.0.0", Source, v1);
        string signedPlugin = Compile("signed", "Plugin", "1.0.0.0", Source, ImmutableArray.Create(Convert.FromHexString(PublicKey)), v1);

        string byName = Granting("by-name", "Plugin");
        string byKey = Granting("by-key", $"Plugin, PublicKey={PublicKey}");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(PackageApiChecker.Check([plugin], Host(byName)), Is.Empty);
            Assert.That(PackageApiChecker.Check([signedPlugin], Host(byKey)), Is.Empty);
            Assert.That(
                PackageApiChecker.Check([plugin], Host(byKey)),
                Is.EqualTo(new PackageApiProblem[] { new(PackageApiProblemKind.MissingType, "HostLib", "HostLib.MadeInternal") }));
        }

        string Granting(string directory, string friend) => Compile(directory, "HostLib", "2.0.0.0", $$"""
            [assembly: System.Runtime.CompilerServices.InternalsVisibleTo("{{friend}}")]
            namespace HostLib;
            internal class MadeInternal { }
            """);
    }

    [Test]
    public void Accepts_the_internal_IsExternalInit_that_a_host_init_accessor_carries()
    {
        string host = Compile("v1", "HostLib", "1.0.0.0", """
            namespace System.Runtime.CompilerServices { internal static class IsExternalInit { } }
            namespace HostLib { public class Widget { public int Size { get; init; } } }
            """);
        string plugin = Compile("plugin", "Plugin", "1.0.0.0", "public class Uses { public object W = new HostLib.Widget { Size = 1 }; }", host);

        Assert.That(PackageApiChecker.Check([plugin], Host(host)), Is.Empty);
    }

    [Test]
    public void Checks_only_references_into_the_host_and_reports_a_host_assembly_that_is_missing()
    {
        string other = Compile("other", "OtherLib", "1.0.0.0", "namespace OtherLib; public class Thing { }");
        string helper = Compile("plugin", "Plugin.Helper", "1.0.0.0", "public class Helper { }");
        string plugin = Compile("plugin", "Plugin", "1.0.0.0", """
            public class Uses
            {
                public OtherLib.Thing? Thing;
                public Helper? Helper;
            }
            """, other, helper);
        Dictionary<string, string?> hostLackingOtherLib = Host();
        hostLackingOtherLib["OtherLib"] = null;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(PackageApiChecker.Check([plugin, helper], Host()), Is.Empty);
            Assert.That(
                PackageApiChecker.Check([plugin, helper], hostLackingOtherLib),
                Is.EqualTo(new PackageApiProblem[] { new(PackageApiProblemKind.MissingAssembly, "OtherLib", "OtherLib") }));
            Assert.That(
                PackageApiChecker.Check([plugin, helper], new Dictionary<string, string?>(Host()) { ["otherlib"] = null }),
                Is.EqualTo(new PackageApiProblem[] { new(PackageApiProblemKind.MissingAssembly, "OtherLib", "OtherLib") }));
        }
    }

    // The host provides the framework and the given assemblies.
    private static Dictionary<string, string?> Host(params string[] assemblies)
    {
        var host = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["System.Private.CoreLib"] = typeof(object).Assembly.Location,
        };
        foreach (string path in assemblies)
        {
            host[Path.GetFileNameWithoutExtension(path)] = path;
        }

        return host;
    }

    private (string HostLib, string Core) CompileV2()
    {
        string core = Compile("v2", "HostLib.Core", "2.0.0.0", HostV2Core);
        return (Compile("v2", "HostLib", "2.0.0.0", HostV2, core), core);
    }

    private string Compile(string directory, string name, string version, string source, params string[] references)
    {
        return Compile(directory, name, version, source, publicKey: default, references);
    }

    // A publicKey makes a public-signed assembly with that key.
    private string Compile(
        string directory, string name, string version, string source, ImmutableArray<byte> publicKey, params string[] references)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            name,
            [
                CSharpSyntaxTree.ParseText(source),
                CSharpSyntaxTree.ParseText($"[assembly: System.Reflection.AssemblyVersion(\"{version}\")]"),
            ],
            [s_coreLibrary, .. references.Select(reference => MetadataReference.CreateFromFile(reference))],
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable,
                cryptoPublicKey: publicKey,
                publicSign: !publicKey.IsDefault));
        string path = Path.Combine(Directory.CreateDirectory(Path.Combine(_root, directory)).FullName, $"{name}.dll");
        EmitResult result = compilation.Emit(path);
        Assert.That(result.Success, Is.True, () => string.Join(Environment.NewLine, result.Diagnostics));
        return path;
    }
}
