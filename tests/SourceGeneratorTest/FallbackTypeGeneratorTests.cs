using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SourceGeneratorTest;

/// <summary>
/// Drives <c>FallbackTypeGenerator</c>. The kept Class1.cs inputs do not implement
/// <c>Beutl.Serialization.IFallback</c>, so the baseline produces no fallback source; a dedicated
/// scenario source (a partial class implementing IFallback) exercises the actual emission path.
/// </summary>
[TestFixture]
public class FallbackTypeGeneratorTests
{
    /// <summary>
    /// Stubs for the IFallback contract the generator looks up plus a base type whose virtual
    /// members the generated fallback overrides, and a partial class implementing IFallback.
    /// </summary>
    private const string FallbackScenario = """
        namespace Beutl.Serialization
        {
            public enum FallbackReason { Unknown }

            public interface ICoreSerializationContext { }

            public interface ICoreSerializable
            {
                void Serialize(ICoreSerializationContext context);
                void Deserialize(ICoreSerializationContext context);
            }

            [System.AttributeUsage(System.AttributeTargets.Class, Inherited = false)]
            public sealed class SuppressFallbackGenerationAttribute : System.Attribute { }

            public interface IJsonSerializationContext : ICoreSerializationContext
            {
                void SetJsonObject(System.Text.Json.Nodes.JsonObject json);
                System.Text.Json.Nodes.JsonObject? GetJsonObject();
            }

            public interface IFallback : ICoreSerializable
            {
                System.Text.Json.Nodes.JsonObject? Json { get; set; }
                FallbackReason Reason { get; set; }
                string? ErrorMessage { get; set; }
                bool TryGetTypeName(out string? result);
            }
        }

        namespace FallbackScenario
        {
            public abstract class Serializable : Beutl.Serialization.ICoreSerializable
            {
                public virtual void Serialize(Beutl.Serialization.ICoreSerializationContext context) { }
                public virtual void Deserialize(Beutl.Serialization.ICoreSerializationContext context) { }
            }

            public partial class FallbackObject : Serializable, Beutl.Serialization.IFallback
            {
            }

            // The generated TryGetTypeName calls Json?.TryGetDiscriminator(out result). The real engine
            // resolves Beutl.JsonHelper's JsonNode extension via an enclosing Beutl.* namespace; the
            // scenario is not under Beutl, so co-locate a matching stub to keep the emitted code compiling.
            public static class JsonDiscriminatorExtensions
            {
                public static bool TryGetDiscriminator(this System.Text.Json.Nodes.JsonNode node, out string? result)
                {
                    result = null;
                    return false;
                }
            }
        }
        """;

    [Test]
    public void ExplicitlySuppressedFallbackImplementation_IsNotOverwritten()
    {
        string source = FallbackScenario.Replace(
            "public partial class FallbackObject : Serializable, Beutl.Serialization.IFallback\n    {\n    }",
            """
            [Beutl.Serialization.SuppressFallbackGeneration]
            public partial class FallbackObject : Serializable, Beutl.Serialization.IFallback
                {
                    public System.Text.Json.Nodes.JsonObject? Json { get; set; }
                    public Beutl.Serialization.FallbackReason Reason { get; set; }
                    public string? ErrorMessage { get; set; }
                    public bool TryGetTypeName(out string? result) { result = null; return false; }
                    public override void Serialize(Beutl.Serialization.ICoreSerializationContext context) { }
                    public override void Deserialize(Beutl.Serialization.ICoreSerializationContext context) { }
                }
            """);
        GeneratorHarnessResult result = GeneratorDriverHarness.Run(source);
        Assert.That(result.HasSource("_Fallback.g.cs"), Is.False);
        Assert.That(result.CompilationErrors, Is.Empty);
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public void ManualMetadataWithoutExplicitOptOut_IsNotSilentlyAccepted(bool serialize, bool deserialize)
    {
        string members = """
            public System.Text.Json.Nodes.JsonObject? Json { get; set; }
            public Beutl.Serialization.FallbackReason Reason { get; set; }
            public string? ErrorMessage { get; set; }
            public bool TryGetTypeName(out string? result) { result = null; return false; }
            """;
        if (serialize) members += "\npublic override void Serialize(Beutl.Serialization.ICoreSerializationContext context) { }";
        if (deserialize) members += "\npublic override void Deserialize(Beutl.Serialization.ICoreSerializationContext context) { }";
        string source = FallbackScenario.Replace(
            "public partial class FallbackObject : Serializable, Beutl.Serialization.IFallback\n    {\n    }",
            $"public partial class FallbackObject : Serializable, Beutl.Serialization.IFallback {{ {members} }}");

        GeneratorHarnessResult result = GeneratorDriverHarness.Run(source);

        Assert.That(result.HasSource("_Fallback.g.cs"), Is.True);
        // Manually declaring generated members requires an explicit opt-out. It must not compile
        // silently with ordinary inherited serialization that drops the retained payload.
        Assert.That(result.CompilationErrors, Is.Not.Empty);
    }

    [Test]
    public void InheritedMetadataWithoutExplicitOptOut_RoundTripsUnknownPayload()
    {
        string source = FallbackScenario.Replace(
            "public abstract class Serializable : Beutl.Serialization.ICoreSerializable\n    {",
            """
            public abstract class Serializable : Beutl.Serialization.ICoreSerializable
                {
                    public System.Text.Json.Nodes.JsonObject? Json { get; set; }
                    public Beutl.Serialization.FallbackReason Reason { get; set; }
                    public string? ErrorMessage { get; set; }
                    public bool TryGetTypeName(out string? result) { result = null; return false; }
            """) + """

            namespace FallbackScenario
            {
                public class JsonContext : Beutl.Serialization.IJsonSerializationContext
                {
                    private System.Text.Json.Nodes.JsonObject _json = new();
                    public System.Text.Json.Nodes.JsonObject GetJsonObject() => _json;
                    public void SetJsonObject(System.Text.Json.Nodes.JsonObject json) => _json = json.DeepClone().AsObject();
                }

                public static class PayloadProbe
                {
                    public static bool RoundTrip()
                    {
                        var payload = System.Text.Json.Nodes.JsonNode.Parse("{\"PluginPayload\":{\"Extra\":17}}")!.AsObject();
                        var input = new JsonContext();
                        input.SetJsonObject(payload);
                        var output = new JsonContext();
                        var fallback = new FallbackObject();
                        fallback.Deserialize(input);
                        fallback.Serialize(output);
                        return System.Text.Json.Nodes.JsonNode.DeepEquals(payload, output.GetJsonObject());
                    }
                }
            }
            """;
        GeneratorHarnessResult result = GeneratorDriverHarness.Run(source);
        Assert.That(result.CompilationErrors, Is.Empty);
        var options = new CSharpParseOptions(LanguageVersion.Preview);
        var trees = new[] { source }.Concat(result.GeneratedSources
            .Where(pair => pair.Key.EndsWith("_Fallback.g.cs", StringComparison.Ordinal)).Select(pair => pair.Value))
            .Select(text => CSharpSyntaxTree.ParseText(text, options));
        var compilation = CSharpCompilation.Create("FallbackRoundTrip", trees, CompilationReferences.Framework,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        image.Position = 0;
        var loadContext = new AssemblyLoadContext("FallbackRoundTrip", isCollectible: true);
        try
        {
            var assembly = loadContext.LoadFromStream(image);
            Assert.That(assembly.GetType("FallbackScenario.PayloadProbe")!.GetMethod("RoundTrip")!.Invoke(null, null), Is.True);
        }
        finally
        {
            loadContext.Unload();
        }
    }

    [Test]
    public void IFallbackImplementer_GeneratesCompilableFallbackPartial()
    {
        GeneratorHarnessResult result = GeneratorDriverHarness.Run(FallbackScenario);

        Assert.That(result.HasSource("_Fallback.g.cs"), Is.True,
            "A partial class implementing IFallback must get a generated fallback partial.");

        string source = result.GetSource("_Fallback.g.cs");
        Assert.Multiple(() =>
        {
            Assert.That(
                result.GeneratorDiagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error),
                Is.Empty, "The generators must run without errors.");
            Assert.That(result.CompilationErrors, Is.Empty,
                "Generated fallback + Resource sources must compile against the stubs: "
                + string.Join(Environment.NewLine, result.CompilationErrors.Select(d => d.ToString())));
            Assert.That(source, Does.Contain("partial class FallbackObject : global::Beutl.Serialization.IFallback"));
            Assert.That(source, Does.Contain("public global::System.Text.Json.Nodes.JsonObject? Json"));
            Assert.That(source, Does.Contain("public global::Beutl.Serialization.FallbackReason Reason"));
            Assert.That(source, Does.Contain("public string? ErrorMessage"));
            Assert.That(source, Does.Contain("public override void Serialize("));
            Assert.That(source, Does.Contain("public override void Deserialize("));
            Assert.That(source, Does.Contain("public bool TryGetTypeName("));
        });
    }
}
