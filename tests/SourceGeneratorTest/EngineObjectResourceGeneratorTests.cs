namespace SourceGeneratorTest;

/// <summary>
/// Drives <c>EngineObjectResourceGenerator</c> against the kept inputs (Class1.cs: Derived /
/// Derived2 / Derived3 : EngineObject) plus the minimal framework stubs, and asserts on the
/// generated <c>Resource</c> nested class and <c>ScanPropertiesCore</c> body.
/// </summary>
[TestFixture]
public class EngineObjectResourceGeneratorTests
{
    private static GeneratorHarnessResult Run() => GeneratorDriverHarness.Run();

    [Test]
    public void Generator_RunsWithoutErrorDiagnostics()
    {
        GeneratorHarnessResult result = Run();

        Assert.That(
            result.GeneratorDiagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error),
            Is.Empty,
            "The generator must run clean against the kept inputs (the 'keep the generator green' gate).");
    }

    [Test]
    public void Generator_EmitsResourceSourcesForEveryDerivedType()
    {
        GeneratorHarnessResult result = Run();

        Assert.Multiple(() =>
        {
            Assert.That(result.HasSource("Derived_Resource.g.cs"), Is.True, "Derived should get a Resource.");
            Assert.That(result.HasSource("Derived2_Resource.g.cs"), Is.True, "Derived2 should get a Resource.");
            Assert.That(result.HasSource("Derived3_Resource.g.cs"), Is.True, "Derived3 should get a Resource.");
        });
    }

    [Test]
    public void Derived_GeneratesNestedResourceClassWithValueProperties()
    {
        string source = Run().GetSource("Derived_Resource.g.cs");

        Assert.Multiple(() =>
        {
            // Nested Resource class deriving from EngineObject.Resource.
            Assert.That(source, Does.Contain("partial class Resource"));
            Assert.That(source, Does.Contain("global::Beutl.Engine.EngineObject.Resource"));

            // Value properties X and Y are surfaced on the Resource.
            Assert.That(source, Does.Contain("public float X"));
            Assert.That(source, Does.Contain("public float Y"));

            // Update override compares-and-updates each value property.
            Assert.That(source, Does.Contain("public override void Update"));
            Assert.That(source, Does.Contain("CompareAndUpdate(context"));
        });
    }

    [Test]
    public void Derived_GeneratesScanPropertiesCoreYieldingEachProperty()
    {
        string source = Run().GetSource("Derived_Resource.g.cs");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("ScanPropertiesCore"));
            Assert.That(source, Does.Contain("yield return X;"));
            Assert.That(source, Does.Contain("yield return Y;"));
            Assert.That(source, Does.Contain("X.SetAttributes(\"X\", __attrs_X);"));
            Assert.That(source, Does.Contain("Y.SetAttributes(\"Y\", __attrs_Y);"));
        });
    }

    [Test]
    public void Derived2_ResourceDerivesFromBaseDerivedResource()
    {
        string source = Run().GetSource("Derived2_Resource.g.cs");

        Assert.Multiple(() =>
        {
            // The Resource inherits the immediate base type's Resource, not EngineObject.Resource.
            Assert.That(source, Does.Contain("Derived.Resource"));
            Assert.That(source, Does.Contain("public float Z"));
            Assert.That(source, Does.Contain("yield return Z;"));
        });
    }

    [Test]
    public void Derived3_GeneratesObjectPropertyForEngineObjectTypedProperty()
    {
        string source = Run().GetSource("Derived3_Resource.g.cs");

        Assert.Multiple(() =>
        {
            // Child is IProperty<Derived> (an EngineObject subtype) -> object property,
            // surfaced as a Derived.Resource and compared via CompareAndUpdateObject.
            Assert.That(source, Does.Contain("Child"));
            Assert.That(source, Does.Contain("CompareAndUpdateObject(context"));
            // The disposable object property is released by its backing field in Dispose.
            Assert.That(source, Does.Contain("_child?.Dispose();"));
        });
    }
}
