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
            Assert.That(derived, Does.Contain("public override void Reconcile"));
            Assert.That(derived, Does.Contain("ResourceReconciler.ReconcileValue(this, context"));
            Assert.That(derived, Does.Contain("ScanPropertiesCore"));
            Assert.That(derived, Does.Contain("yield return X;"));
            Assert.That(derived, Does.Contain("yield return Y;"));
            Assert.That(derived, Does.Contain("X.SetAttributes(\"X\", __attrs_X);"));
            Assert.That(derived, Does.Contain("Y.SetAttributes(\"Y\", __attrs_Y);"));

            Assert.That(derived, Does.Contain("public global::SourceGeneratorTest.TextAlignment TextAlignment"));
            Assert.That(derived, Does.Contain("set => _textAlignment = value;"));
            Assert.That(derived, Does.Contain(
                "ResourceReconciler.ReconcileValue(this, context, ((global::SourceGeneratorTest.Derived)obj).TextAlignment, ref _textAlignment, ref versionBumped);"));
            Assert.That(derived, Does.Contain("yield return TextAlignment;"));
            Assert.That(derived, Does.Contain("TextAlignment.SetAttributes(\"TextAlignment\", __attrs_TextAlignment);"));

            Assert.That(derived2, Does.Contain("Derived.Resource"));
            Assert.That(derived2, Does.Contain("public float Z"));
            Assert.That(derived2, Does.Contain("yield return Z;"));
            Assert.That(derived2, Does.Contain("public float W"));
            Assert.That(derived2, Does.Contain("yield return W;"));
            Assert.That(derived2, Does.Contain("VProperty.SetAttributes(\"VProperty\", __attrs_VProperty);"));

            Assert.That(derived3, Does.Contain("Child"));
            Assert.That(derived3, Does.Contain("ResourceReconciler.ReconcileChild(this, context"));
            Assert.That(derived3, Does.Contain("set => _child = value;"));
            Assert.That(derived3, Does.Contain("set => _optionalChild = value;"));
            Assert.That(derived3, Does.Contain(
                "get => _child ?? throw new global::System.InvalidOperationException"));
            Assert.That(derived3, Does.Contain("_child?.Dispose();"));
            Assert.That(derived3, Does.Contain("Items"));
            Assert.That(derived3, Does.Contain("ResourceReconciler.ReconcileChildren(this, context"));
            Assert.That(derived3, Does.Contain("foreach (var item in"));
            Assert.That(derived3, Does.Contain("item.Dispose();"));
        });
    }

    private const string CoreObjects = """
        namespace SourceGeneratorTest;

        public partial class Settings : CoreObject
        {
            public IProperty<int> Count { get; } = Beutl.Engine.Property.Create(0);

            internal IProperty<int> Internal { get; } = Beutl.Engine.Property.Create(0);
        }

        public partial class EngineType : Beutl.Engine.EngineObject
        {
            public IProperty<int> Shown { get; } = Beutl.Engine.Property.Create(0);

            internal IProperty<int> Internal { get; } = Beutl.Engine.Property.Create(0);
        }

        public class Unmarked : CoreObject
        {
            public IProperty<int> Count { get; } = Beutl.Engine.Property.Create(0);
        }

        public class Empty : CoreObject
        {
        }

        public class Outer
        {
            public partial class Nested : CoreObject
            {
                public IProperty<int> Count { get; } = Beutl.Engine.Property.Create(0);
            }
        }
        """;

    [Test]
    public void ACoreObjectThatIsNotAnEngineObject_GetsOnlyItsPropertyScan()
    {
        GeneratorHarnessResult result = GeneratorDriverHarness.Run(CoreObjects);
        string settings = result.GetSource("Settings_Properties.g.cs");
        string engine = result.GetSource("EngineType_Resource.g.cs");

        Assert.Multiple(() =>
        {
            Assert.That(result.CompilationErrors, Is.Empty,
                string.Join(Environment.NewLine, result.CompilationErrors.Select(d => d.ToString())));
            Assert.That(settings, Does.Contain("Count.SetAttributes(\"Count\", __attrs_Count);"));
            Assert.That(settings, Does.Not.Contain("Internal"));
            Assert.That(settings, Does.Not.Contain("Resource"));
            Assert.That(engine, Does.Contain("yield return Shown;"));
            Assert.That(engine, Does.Not.Contain("Internal"));
            Assert.That(result.HasSource("Empty_Properties.g.cs"), Is.False);
            Assert.That(result.HasSource("Nested_Properties.g.cs"), Is.False);
            Assert.That(
                result.GeneratorDiagnostics.Where(d => d.Id == "BESG001").Select(d => d.GetMessage()),
                Is.EqualTo(new[] { "Type 'SourceGeneratorTest.Unmarked' must be declared partial to generate its property scan" }));
        });
    }
}
