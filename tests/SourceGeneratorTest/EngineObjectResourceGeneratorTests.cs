using Microsoft.CodeAnalysis;

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
    public void ConstructorAssignedResourceProperty_ReportsACompileTimeDiagnostic()
    {
        const string source = """
            namespace SourceGeneratorTest;

            public partial class ConstructorAssigned : Beutl.Engine.EngineObject
            {
                public Beutl.Engine.IProperty<float> Value { get; }

                public ConstructorAssigned()
                {
                    Value = Beutl.Engine.Property.Create(42f);
                }
            }
            """;

        GeneratorHarnessResult result = GeneratorDriverHarness.Run(source);
        Diagnostic diagnostic = result.GeneratorDiagnostics.Single(item => item.Id == "BESG003");

        Assert.Multiple(() =>
        {
            Assert.That(diagnostic.Severity, Is.EqualTo(DiagnosticSeverity.Error));
            Assert.That(diagnostic.GetMessage(), Does.Contain("ConstructorAssigned.Value"));
            Assert.That(result.HasSource("ConstructorAssigned_Resource.g.cs"), Is.False);
        });
    }

    [Test]
    public void ComputedResourcePropertyBackedByDeclarationState_Generates()
    {
        const string source = """
            namespace SourceGeneratorTest;

            public partial class ComputedProperty : Beutl.Engine.EngineObject
            {
                private readonly Beutl.Engine.IProperty<float> _value
                    = Beutl.Engine.Property.Create(42f);

                public Beutl.Engine.IProperty<float> Value => _value;
            }
            """;

        GeneratorHarnessResult result = GeneratorDriverHarness.Run(source);

        Assert.Multiple(() =>
        {
            Assert.That(result.GeneratorDiagnostics.Any(item => item.Id == "BESG003"), Is.False);
            Assert.That(result.HasSource("ComputedProperty_Resource.g.cs"), Is.True);
            Assert.That(result.CompilationErrors, Is.Empty,
                string.Join(Environment.NewLine, result.CompilationErrors.Select(static item => item.ToString())));
        });
    }

    [Test]
    public void DeclarationInitializedPropertyReassignedInConstructor_ReportsACompileTimeDiagnostic()
    {
        const string source = """
            namespace SourceGeneratorTest;

            public partial class ReassignedProperty : Beutl.Engine.EngineObject
            {
                public Beutl.Engine.IProperty<float> Value { get; }
                    = Beutl.Engine.Property.Create(1f);

                public ReassignedProperty()
                {
                    Value = Beutl.Engine.Property.Create(42f);
                }
            }
            """;

        GeneratorHarnessResult result = GeneratorDriverHarness.Run(source);
        Diagnostic diagnostic = result.GeneratorDiagnostics.Single(item => item.Id == "BESG003");

        Assert.Multiple(() =>
        {
            Assert.That(diagnostic.Severity, Is.EqualTo(DiagnosticSeverity.Error));
            Assert.That(diagnostic.GetMessage(), Does.Contain("ReassignedProperty.Value"));
            Assert.That(diagnostic.GetMessage(), Does.Contain("do not replace it in a constructor"));
            Assert.That(result.HasSource("ReassignedProperty_Resource.g.cs"), Is.False);
        });
    }

    [Test]
    public void PrimaryConstructor_ReportsACompileTimeDiagnostic()
    {
        const string source = """
            namespace SourceGeneratorTest;

            public partial class PrimaryConstructor(int value) : Beutl.Engine.EngineObject
            {
                public int Value { get; } = value;

                public Beutl.Engine.IProperty<float> Amount { get; }
                    = Beutl.Engine.Property.Create(42f);
            }
            """;

        GeneratorHarnessResult result = GeneratorDriverHarness.Run(source);
        Diagnostic diagnostic = result.GeneratorDiagnostics.Single(item => item.Id == "BESG004");

        Assert.Multiple(() =>
        {
            Assert.That(diagnostic.Severity, Is.EqualTo(DiagnosticSeverity.Error));
            Assert.That(diagnostic.GetMessage(), Does.Contain("SourceGeneratorTest.PrimaryConstructor"));
            Assert.That(diagnostic.GetMessage(), Does.Contain("ordinary constructor"));
            Assert.That(diagnostic.GetMessage(), Does.Contain("suppress Resource generation"));
            Assert.That(result.HasSource("PrimaryConstructor_Resource.g.cs"), Is.False);
        });
    }

    [Test]
    public void RequiredUnrelatedMember_DoesNotBlockTheGeneratorOnlyConstructionPath()
    {
        const string source = """
            namespace SourceGeneratorTest;

            public partial class RequiredMember : Beutl.Engine.EngineObject
            {
                public required string Label { get; init; }

                public Beutl.Engine.IProperty<float> Amount { get; }
                    = Beutl.Engine.Property.Create(42f);
            }
            """;

        GeneratorHarnessResult result = GeneratorDriverHarness.Run(source);
        string generated = result.GetSource("RequiredMember_Resource.g.cs");

        Assert.Multiple(() =>
        {
            Assert.That(generated, Does.Contain(
                "[global::System.Diagnostics.CodeAnalysis.SetsRequiredMembers]"));
            Assert.That(result.CompilationErrors, Is.Empty,
                string.Join(Environment.NewLine, result.CompilationErrors.Select(static item => item.ToString())));
        });
    }

    [Test]
    public void PrimaryConstructor_WithExplicitDefaultsProvider_Generates()
    {
        const string source = """
            namespace SourceGeneratorTest;

            public partial class ProviderBackedPrimary(int value) : Beutl.Engine.EngineObject
            {
                public Beutl.Engine.IProperty<int> Value { get; }
                    = Beutl.Engine.Property.Create(value);

                [Beutl.Engine.ResourceDefaultValuesProvider]
                private static ProviderBackedPrimary CreateResourceDefaults() => new(42);
            }
            """;

        GeneratorHarnessResult result = GeneratorDriverHarness.Run(source);
        string generated = result.GetSource("ProviderBackedPrimary_Resource.g.cs");

        Assert.Multiple(() =>
        {
            Assert.That(result.GeneratorDiagnostics.Any(item => item.Severity == DiagnosticSeverity.Error), Is.False);
            Assert.That(generated, Does.Contain(
                "global::SourceGeneratorTest.ProviderBackedPrimary.CreateResourceDefaults()"));
            Assert.That(generated, Does.Not.Contain("ResourceDefaultValuesConstruction construction"));
            Assert.That(result.CompilationErrors, Is.Empty,
                string.Join(Environment.NewLine, result.CompilationErrors.Select(static item => item.ToString())));
        });
    }

    [Test]
    public void ConstructorAssignedProperty_WithExplicitDefaultsProvider_Generates()
    {
        const string source = """
            namespace SourceGeneratorTest;

            public partial class ProviderBackedConstructor : Beutl.Engine.EngineObject
            {
                public Beutl.Engine.IProperty<float> Value { get; }

                private ProviderBackedConstructor(float value)
                {
                    Value = Beutl.Engine.Property.Create(value);
                }

                [Beutl.Engine.ResourceDefaultValuesProvider]
                private static ProviderBackedConstructor CreateResourceDefaults() => new(42f);
            }
            """;

        GeneratorHarnessResult result = GeneratorDriverHarness.Run(source);
        string generated = result.GetSource("ProviderBackedConstructor_Resource.g.cs");

        Assert.Multiple(() =>
        {
            Assert.That(result.GeneratorDiagnostics.Any(item => item.Severity == DiagnosticSeverity.Error), Is.False);
            Assert.That(generated, Does.Contain(
                "global::SourceGeneratorTest.ProviderBackedConstructor.CreateResourceDefaults()"));
            Assert.That(result.CompilationErrors, Is.Empty,
                string.Join(Environment.NewLine, result.CompilationErrors.Select(static item => item.ToString())));
        });
    }

    [Test]
    public void InvalidDefaultsProviderSignature_ReportsACompileTimeDiagnostic()
    {
        const string source = """
            namespace SourceGeneratorTest;

            public partial class InvalidProvider : Beutl.Engine.EngineObject
            {
                public Beutl.Engine.IProperty<float> Value { get; }
                    = Beutl.Engine.Property.Create(1f);

                [Beutl.Engine.ResourceDefaultValuesProvider]
                private static InvalidProvider CreateResourceDefaults(float value) => new();
            }
            """;

        GeneratorHarnessResult result = GeneratorDriverHarness.Run(source);
        Diagnostic diagnostic = result.GeneratorDiagnostics.Single(item => item.Id == "BESG005");

        Assert.Multiple(() =>
        {
            Assert.That(diagnostic.Severity, Is.EqualTo(DiagnosticSeverity.Error));
            Assert.That(diagnostic.GetMessage(), Does.Contain("static, parameterless, non-generic"));
            Assert.That(result.HasSource("InvalidProvider_Resource.g.cs"), Is.False);
        });
    }

    [Test]
    public void NullableDefaultsProviderReturn_ReportsACompileTimeDiagnostic()
    {
        const string source = """
            #nullable enable
            namespace SourceGeneratorTest;

            public partial class NullableProvider : Beutl.Engine.EngineObject
            {
                public Beutl.Engine.IProperty<float> Value { get; }
                    = Beutl.Engine.Property.Create(1f);

                [Beutl.Engine.ResourceDefaultValuesProvider]
                private static NullableProvider? CreateResourceDefaults() => null;
            }
            """;

        GeneratorHarnessResult result = GeneratorDriverHarness.Run(source);

        Assert.Multiple(() =>
        {
            Assert.That(result.GeneratorDiagnostics.Single(item => item.Id == "BESG005").Severity,
                Is.EqualTo(DiagnosticSeverity.Error));
            Assert.That(result.HasSource("NullableProvider_Resource.g.cs"), Is.False);
        });
    }

    [Test]
    public void DerivedTypeWithoutItsOwnDefaultsProvider_ReportsACompileTimeDiagnostic()
    {
        const string source = """
            namespace SourceGeneratorTest;

            public partial class ProviderBase : Beutl.Engine.EngineObject
            {
                public Beutl.Engine.IProperty<float> BaseValue { get; }
                    = Beutl.Engine.Property.Create(1f);

                [Beutl.Engine.ResourceDefaultValuesProvider]
                private static ProviderBase CreateResourceDefaults() => new();
            }

            public partial class MissingDerivedProvider : ProviderBase
            {
                public Beutl.Engine.IProperty<float> DerivedValue { get; }
                    = Beutl.Engine.Property.Create(2f);
            }
            """;

        GeneratorHarnessResult result = GeneratorDriverHarness.Run(source);
        Diagnostic diagnostic = result.GeneratorDiagnostics.Single(item => item.Id == "BESG006");

        Assert.Multiple(() =>
        {
            Assert.That(diagnostic.Severity, Is.EqualTo(DiagnosticSeverity.Error));
            Assert.That(diagnostic.GetMessage(), Does.Contain("SourceGeneratorTest.MissingDerivedProvider"));
            Assert.That(result.HasSource("ProviderBase_Resource.g.cs"), Is.True);
            Assert.That(result.HasSource("MissingDerivedProvider_Resource.g.cs"), Is.False);
        });
    }

    [Test]
    public void DerivedTypeWithItsOwnDefaultsProvider_Generates()
    {
        const string source = """
            namespace SourceGeneratorTest;

            public partial class ProviderBaseWithDerived : Beutl.Engine.EngineObject
            {
                public Beutl.Engine.IProperty<float> BaseValue { get; }
                    = Beutl.Engine.Property.Create(1f);

                [Beutl.Engine.ResourceDefaultValuesProvider]
                private static ProviderBaseWithDerived CreateResourceDefaults() => new();
            }

            public partial class ProviderDerived : ProviderBaseWithDerived
            {
                public Beutl.Engine.IProperty<float> DerivedValue { get; }
                    = Beutl.Engine.Property.Create(2f);

                [Beutl.Engine.ResourceDefaultValuesProvider]
                private static ProviderDerived CreateResourceDefaults() => new();
            }
            """;

        GeneratorHarnessResult result = GeneratorDriverHarness.Run(source);

        Assert.Multiple(() =>
        {
            Assert.That(result.GeneratorDiagnostics.Any(item => item.Severity == DiagnosticSeverity.Error), Is.False);
            Assert.That(result.HasSource("ProviderBaseWithDerived_Resource.g.cs"), Is.True);
            Assert.That(result.HasSource("ProviderDerived_Resource.g.cs"), Is.True);
            Assert.That(result.CompilationErrors, Is.Empty,
                string.Join(Environment.NewLine, result.CompilationErrors.Select(static item => item.ToString())));
        });
    }

    [Test]
    public void ComputedResourcePropertyBackedByConstructorState_ReportsACompileTimeDiagnostic()
    {
        const string source = """
            namespace SourceGeneratorTest;

            public partial class ConstructorBackedComputedProperty : Beutl.Engine.EngineObject
            {
                private readonly Beutl.Engine.IProperty<float> _value;

                public ConstructorBackedComputedProperty()
                {
                    _value = Beutl.Engine.Property.Create(42f);
                }

                public Beutl.Engine.IProperty<float> Value => _value;
            }
            """;

        GeneratorHarnessResult result = GeneratorDriverHarness.Run(source);
        Diagnostic diagnostic = result.GeneratorDiagnostics.Single(item => item.Id == "BESG003");

        Assert.Multiple(() =>
        {
            Assert.That(diagnostic.Severity, Is.EqualTo(DiagnosticSeverity.Error));
            Assert.That(diagnostic.GetMessage(), Does.Contain("ConstructorBackedComputedProperty.Value"));
            Assert.That(result.HasSource("ConstructorBackedComputedProperty_Resource.g.cs"), Is.False);
        });
    }

    [Test]
    public void DerivedConstructorReassigningInheritedResourceProperty_ReportsACompileTimeDiagnostic()
    {
        const string source = """
            namespace SourceGeneratorTest;

            public partial class StableBaseProperty : Beutl.Engine.EngineObject
            {
                public Beutl.Engine.IProperty<float> Value { get; protected set; }
                    = Beutl.Engine.Property.Create(1f);
            }

            public partial class ReassigningDerivedProperty : StableBaseProperty
            {
                public ReassigningDerivedProperty()
                {
                    Value = Beutl.Engine.Property.Create(42f);
                }
            }
            """;

        GeneratorHarnessResult result = GeneratorDriverHarness.Run(source);
        Diagnostic diagnostic = result.GeneratorDiagnostics.Single(item => item.Id == "BESG003");

        Assert.Multiple(() =>
        {
            Assert.That(diagnostic.Severity, Is.EqualTo(DiagnosticSeverity.Error));
            Assert.That(diagnostic.GetMessage(), Does.Contain("ReassigningDerivedProperty.Value"));
            Assert.That(result.HasSource("StableBaseProperty_Resource.g.cs"), Is.True);
            Assert.That(result.HasSource("ReassigningDerivedProperty_Resource.g.cs"), Is.False);
        });
    }

    [Test]
    public void ConstructorDeconstructingResourceProperty_ReportsACompileTimeDiagnostic()
    {
        const string source = """
            namespace SourceGeneratorTest;

            public partial class DeconstructingProperty : Beutl.Engine.EngineObject
            {
                private int _other;

                public Beutl.Engine.IProperty<float> Value { get; private set; }
                    = Beutl.Engine.Property.Create(1f);

                public DeconstructingProperty()
                {
                    (Value, _other) = (Beutl.Engine.Property.Create(42f), 0);
                }
            }
            """;

        GeneratorHarnessResult result = GeneratorDriverHarness.Run(source);
        Diagnostic diagnostic = result.GeneratorDiagnostics.Single(item => item.Id == "BESG003");

        Assert.Multiple(() =>
        {
            Assert.That(diagnostic.Severity, Is.EqualTo(DiagnosticSeverity.Error));
            Assert.That(diagnostic.GetMessage(), Does.Contain("DeconstructingProperty.Value"));
            Assert.That(result.HasSource("DeconstructingProperty_Resource.g.cs"), Is.False);
        });
    }

    [Test]
    public void DerivedConstructorDeconstructingInheritedResourceProperty_ReportsACompileTimeDiagnostic()
    {
        const string source = """
            namespace SourceGeneratorTest;

            public partial class StableDeconstructionBase : Beutl.Engine.EngineObject
            {
                public Beutl.Engine.IProperty<float> Value { get; protected set; }
                    = Beutl.Engine.Property.Create(1f);
            }

            public partial class DeconstructingDerivedProperty : StableDeconstructionBase
            {
                private int _other;

                public DeconstructingDerivedProperty()
                {
                    (Value, _other) = (Beutl.Engine.Property.Create(42f), 0);
                }
            }
            """;

        GeneratorHarnessResult result = GeneratorDriverHarness.Run(source);
        Diagnostic diagnostic = result.GeneratorDiagnostics.Single(item => item.Id == "BESG003");

        Assert.Multiple(() =>
        {
            Assert.That(diagnostic.Severity, Is.EqualTo(DiagnosticSeverity.Error));
            Assert.That(diagnostic.GetMessage(), Does.Contain("DeconstructingDerivedProperty.Value"));
            Assert.That(result.HasSource("StableDeconstructionBase_Resource.g.cs"), Is.True);
            Assert.That(result.HasSource("DeconstructingDerivedProperty_Resource.g.cs"), Is.False);
        });
    }

    [Test]
    public void AbstractResource_RequiresAnExplicitSafeBaseConstructor()
    {
        const string source = """
            namespace SourceGeneratorTest;

            public abstract partial class AbstractResourceOwner : Beutl.Engine.EngineObject
            {
                public Beutl.Engine.IProperty<float> BaseValue { get; }
                    = Beutl.Engine.Property.Create(7f);
            }

            public sealed partial class ConcreteResourceOwner : AbstractResourceOwner
            {
                public Beutl.Engine.IProperty<float> DerivedValue { get; }
                    = Beutl.Engine.Property.Create(9f);
            }
            """;

        GeneratorHarnessResult result = GeneratorDriverHarness.Run(source);
        string abstractResource = result.GetSource("AbstractResourceOwner_Resource.g.cs");

        Assert.Multiple(() =>
        {
            Assert.That(abstractResource, Does.Not.Contain("protected Resource()"));
            Assert.That(abstractResource, Does.Contain(
                "protected Resource(global::SourceGeneratorTest.AbstractResourceOwner defaultValues)"));
            Assert.That(abstractResource, Does.Contain("protected Resource(bool skipDefaultInitialization)"));
            Assert.That(result.CompilationErrors, Is.Empty,
                string.Join(Environment.NewLine, result.CompilationErrors.Select(static item => item.ToString())));
        });
    }

    [Test]
    public void SuppressedResourceGeneration_DoesNotRequireOrValidateDefaultsProviders()
    {
        const string source = """
            namespace SourceGeneratorTest;

            [Beutl.Engine.SuppressResourceClassGeneration]
            public partial class SuppressedProviderOwner(int value) : Beutl.Engine.EngineObject
            {
                public Beutl.Engine.IProperty<int> Value { get; }
                    = Beutl.Engine.Property.Create(value);

                [Beutl.Engine.ResourceDefaultValuesProvider]
                private SuppressedProviderOwner InvalidProvider(int unused) => this;
            }
            """;

        GeneratorHarnessResult result = GeneratorDriverHarness.Run(source);
        string generated = result.GetSource("SuppressedProviderOwner_Resource.g.cs");

        Assert.Multiple(() =>
        {
            Assert.That(result.GeneratorDiagnostics.Any(item => item.Id is "BESG003" or "BESG004" or "BESG005" or "BESG006"),
                Is.False);
            Assert.That(generated, Does.Not.Contain("partial class Resource"));
            Assert.That(result.CompilationErrors, Is.Empty,
                string.Join(Environment.NewLine, result.CompilationErrors.Select(static item => item.ToString())));
        });
    }

    [Test]
    public void GeneratedSources_CompileWithoutErrors()
    {
        GeneratorHarnessResult result = Run();

        Assert.That(
            result.CompilationErrors,
            Is.Empty,
            "Generated Resource sources must compile against the stub inputs (the real-gate check): "
            + string.Join(Environment.NewLine, result.CompilationErrors.Select(d => d.ToString())));
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
    public void Derived_SeparatesDetachedDefaultsFromTheAttachedFastPath()
    {
        string source = Run().GetSource("Derived_Resource.g.cs");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("public Resource()"));
            Assert.That(source, Does.Contain("#pragma warning disable CS8631, CS8618, CS9264"));
            Assert.That(source, Does.Contain("__CreateResourceDefaultValues()"));
            Assert.That(source, Does.Contain("_y = defaultValues.Y.DefaultValue;"));
            Assert.That(source, Does.Contain("global::SourceGeneratorTest.Derived.Resource.__CreateAttachedDerived()"));
            Assert.That(source, Does.Not.Contain("private float _y = 17"),
                "attached construction must not evaluate detached defaults through field initializers");
        });
    }

    [Test]
    public void Derived_GeneratesNullableTypedOriginalAccess()
    {
        string source = Run().GetSource("Derived_Resource.g.cs");

        Assert.That(source, Does.Contain("public new global::SourceGeneratorTest.Derived? GetOriginal()"));
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
            Assert.That(source, Does.Contain("defaultValues.Child.DefaultValue is { } _childDefaultValue"));
            Assert.That(source, Does.Contain("_childDefaultValue.ToResource(global::Beutl.Composition.CompositionContext.Default)"));
            Assert.That(source, Does.Contain("if (!global::System.Object.ReferenceEquals(_child, value))"));
            Assert.That(source, Does.Contain("oldValue?.Dispose();"));
            // The disposable object property is released by its backing field in Dispose.
            Assert.That(source, Does.Contain("_child?.Dispose();"));
        });
    }

    [Test]
    public void Derived3_GeneratesListPropertyForIListPropertyMember()
    {
        string source = Run().GetSource("Derived3_Resource.g.cs");

        Assert.Multiple(() =>
        {
            // Items is IListProperty<Derived> (an EngineObject element) -> list property: surfaced as a
            // List<Derived.Resource>, reconciled via CompareAndUpdateList, and disposed element-by-element.
            Assert.That(source, Does.Contain("Items"));
            Assert.That(source, Does.Contain("CompareAndUpdateList(context"));
            Assert.That(source, Does.Contain("foreach (var item in"));
            Assert.That(source, Does.Contain("item?.Dispose();"));
        });
    }
}
