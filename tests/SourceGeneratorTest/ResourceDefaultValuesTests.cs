using Microsoft.CodeAnalysis;

namespace SourceGeneratorTest;

/// <summary>
/// Covers the detached-resource defaults contract: the generated constructor chain, the
/// <c>[ResourceDefaultValuesProvider]</c> escape hatch, and the BESG003-BESG006 diagnostics that
/// reject declaration shapes whose defaults cannot be read without running a user constructor.
/// </summary>
[TestFixture]
public class ResourceDefaultValuesTests
{
    private static IEnumerable<Diagnostic> DiagnosticsWithId(GeneratorHarnessResult result, string id)
        => result.GeneratorDiagnostics.Where(d => d.Id == id);

    [Test]
    public void GeneratedResource_ChainsThroughTheDeclaredDefaults()
    {
        string source = GeneratorDriverHarness.Run().GetSource("Derived_Resource.g.cs");

        Assert.Multiple(() =>
        {
            Assert.That(
                source,
                Does.Contain("public Resource()"),
                "A generated concrete Resource keeps a public detached constructor.");
            Assert.That(
                source,
                Does.Contain("__CreateResourceDefaultValues()"),
                "The detached constructor reads its defaults from a generated factory.");
            Assert.That(
                source,
                Does.Contain("protected Resource(bool skipDefaultInitialization)"),
                "The attached path opts out of default evaluation explicitly.");
            Assert.That(
                source,
                Does.Contain("_x = defaultValues.X.DefaultValue;"),
                "Each generated value property starts at its declared default.");
        });
    }

    [Test]
    public void ToResource_UsesTheAttachedFastPath()
    {
        string source = GeneratorDriverHarness.Run().GetSource("Derived_Resource.g.cs");

        Assert.That(
            source,
            Does.Contain("__CreateAttachedDerived()"),
            "ToResource must not evaluate detached defaults that Update immediately replaces.");
    }

    [Test]
    public void PropertyAssignedInAConstructor_ReportsBESG003()
    {
        const string Scenario = """
            using Beutl.Engine;

            namespace SourceGeneratorTest.Scenarios;

            public partial class ConstructorAssigned : EngineObject
            {
                public ConstructorAssigned()
                {
                    Value = Property.Create(1f);
                }

                public IProperty<float> Value { get; private set; }
            }
            """;

        GeneratorHarnessResult result = GeneratorDriverHarness.Run(Scenario);

        Assert.That(
            DiagnosticsWithId(result, "BESG003"),
            Is.Not.Empty,
            "A property whose IProperty is replaced in a constructor has no declaration-time default.");
    }

    [Test]
    public void PrimaryConstructor_ReportsBESG004()
    {
        const string Scenario = """
            using Beutl.Engine;

            namespace SourceGeneratorTest.Scenarios;

            public partial class PrimaryCtor(float seed) : EngineObject
            {
                public IProperty<float> Value { get; } = Property.Create(seed);
            }
            """;

        GeneratorHarnessResult result = GeneratorDriverHarness.Run(Scenario);

        Assert.That(
            DiagnosticsWithId(result, "BESG004"),
            Is.Not.Empty,
            "A primary constructor cannot run on the initializer-only defaults path.");
    }

    [Test]
    public void InvalidProviderSignature_ReportsBESG005()
    {
        const string Scenario = """
            using Beutl.Engine;

            namespace SourceGeneratorTest.Scenarios;

            public partial class BadProvider : EngineObject
            {
                public IProperty<float> Value { get; } = Property.Create(0f);

                [ResourceDefaultValuesProvider]
                private static EngineObject CreateDefaults() => new BadProvider();
            }
            """;

        GeneratorHarnessResult result = GeneratorDriverHarness.Run(Scenario);

        Assert.That(
            DiagnosticsWithId(result, "BESG005"),
            Is.Not.Empty,
            "A provider must return the declaring owner type exactly.");
    }

    [Test]
    public void DerivedTypeWithoutItsOwnProvider_ReportsBESG006()
    {
        const string Scenario = """
            using Beutl.Engine;

            namespace SourceGeneratorTest.Scenarios;

            public partial class ProviderBase : EngineObject
            {
                public IProperty<float> BaseValue { get; } = Property.Create(0f);

                [ResourceDefaultValuesProvider]
                private static ProviderBase CreateDefaults() => new();
            }

            public partial class ProviderDerived : ProviderBase
            {
                public IProperty<float> DerivedValue { get; } = Property.Create(0f);
            }
            """;

        GeneratorHarnessResult result = GeneratorDriverHarness.Run(Scenario);

        Assert.That(
            DiagnosticsWithId(result, "BESG006"),
            Is.Not.Empty,
            "Inheriting a provider would evaluate the base owner's defaults for the derived type.");
    }

    [Test]
    public void ValidProvider_GeneratesWithoutDiagnostics()
    {
        const string Scenario = """
            using Beutl.Engine;

            namespace SourceGeneratorTest.Scenarios;

            public partial class GoodProvider(float seed) : EngineObject
            {
                public IProperty<float> Value { get; } = Property.Create(seed);

                [ResourceDefaultValuesProvider]
                private static GoodProvider CreateDefaults() => new(0f);
            }
            """;

        GeneratorHarnessResult result = GeneratorDriverHarness.Run(Scenario);

        Assert.Multiple(() =>
        {
            Assert.That(
                result.GeneratorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error),
                Is.Empty,
                "A valid provider is the documented escape hatch for a primary constructor.");
            Assert.That(
                result.HasSource("GoodProvider_Resource.g.cs"),
                Is.True,
                "Generation proceeds once a provider supplies the defaults owner.");
        });
    }
}
