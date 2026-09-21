namespace SourceGeneratorTest;

/// <summary>
/// Drives <c>EngineObjectResourceGenerator</c> against the kept inputs (Class1.cs: Derived /
/// Derived2 / Derived3 : EngineObject) plus the minimal framework stubs, and asserts on the
/// generated <c>Resource</c> nested class and <c>ScanPropertiesCore</c> body.
/// </summary>
[TestFixture]
public class EngineObjectResourceGeneratorTests
{
    [Test]
    public void KeptInputs_GenerateCompilableResources()
    {
        GeneratorHarnessResult result = GeneratorDriverHarness.Run();
        string derived = result.GetSource("Derived_Resource.g.cs");
        string derived2 = result.GetSource("Derived2_Resource.g.cs");
        string derived3 = result.GetSource("Derived3_Resource.g.cs");

        // These assertions all inspect the same generated output. Run and bind it once while
        // retaining diagnostics for every contract, including the absence of fallback output.
        Assert.Multiple(() =>
        {
            Assert.That(
                result.GeneratorDiagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error),
                Is.Empty, "The generators must run without errors.");
            Assert.That(result.CompilationErrors, Is.Empty,
                "Generated Resource sources must compile against the stub inputs: "
                + string.Join(Environment.NewLine, result.CompilationErrors.Select(d => d.ToString())));
            Assert.That(result.HasSource("_Fallback.g.cs"), Is.False,
                "None of the kept Derived* inputs implement IFallback.");

            Assert.That(derived, Does.Contain("partial class Resource"));
            Assert.That(derived, Does.Contain("global::Beutl.Engine.EngineObject.Resource"));
            Assert.That(derived, Does.Contain("public float X"));
            Assert.That(derived, Does.Contain("public float Y"));
            Assert.That(derived, Does.Contain("set => _x = value;"));
            Assert.That(derived, Does.Contain("set => _y = value;"));
            Assert.That(derived, Does.Not.Contain("Version++"));
            Assert.That(derived, Does.Contain("public override void Update"));
            Assert.That(derived, Does.Contain("CompareAndUpdate(context"));
            Assert.That(derived, Does.Contain("ScanPropertiesCore"));
            Assert.That(derived, Does.Contain("yield return X;"));
            Assert.That(derived, Does.Contain("yield return Y;"));
            Assert.That(derived, Does.Contain("X.SetAttributes(\"X\", __attrs_X);"));
            Assert.That(derived, Does.Contain("Y.SetAttributes(\"Y\", __attrs_Y);"));

            Assert.That(derived2, Does.Contain("Derived.Resource"));
            Assert.That(derived2, Does.Contain("public float Z"));
            Assert.That(derived2, Does.Contain("yield return Z;"));

            Assert.That(derived3, Does.Contain("Child"));
            Assert.That(derived3, Does.Contain("CompareAndUpdateObject(context"));
            Assert.That(derived3, Does.Contain("set => _child = value;"));
            Assert.That(derived3, Does.Contain("set => _optionalChild = value;"));
            Assert.That(derived3, Does.Not.Contain("SetOwnedResource"));
            Assert.That(derived3, Does.Not.Contain("ReplaceChild("));
            Assert.That(derived3, Does.Not.Contain("DetachChild()"));
            Assert.That(derived3, Does.Contain(
                "get => _child ?? throw new global::System.InvalidOperationException"));
            Assert.That(derived3, Does.Not.Contain("DetachOptionalChild()"));
            Assert.That(derived3, Does.Not.Contain("ReplaceOptionalChild("));
            Assert.That(derived3, Does.Contain("_child?.Dispose();"));
            Assert.That(derived3, Does.Contain("Items"));
            Assert.That(derived3, Does.Contain("CompareAndUpdateList(context"));
            Assert.That(derived3, Does.Contain("foreach (var item in"));
            Assert.That(derived3, Does.Contain("item?.Dispose();"));
        });
    }
}
