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

            public interface IJsonSerializationContext
            {
                void SetJsonObject(System.Text.Json.Nodes.JsonObject json);
                System.Text.Json.Nodes.JsonObject? GetJsonObject();
            }

            public interface IFallback
            {
                System.Text.Json.Nodes.JsonObject? Json { get; set; }
                FallbackReason Reason { get; set; }
                string? ErrorMessage { get; set; }
                bool TryGetTypeName(out string? result);
            }
        }

        namespace FallbackScenario
        {
            public abstract class Serializable
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
