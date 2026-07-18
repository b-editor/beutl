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

    private static GeneratorHarnessResult RunTypedHandwrittenResourceInheritance()
    {
        return GeneratorDriverHarness.Run(
            """
            namespace SourceGeneratorTest;

            [Beutl.Engine.SuppressResourceClassGeneration]
            public partial class HandwrittenResourceBase : Beutl.Engine.EngineObject
            {
                public new class Resource : Beutl.Engine.EngineObject.Resource
                {
                    public sealed override void Update(
                        Beutl.Engine.EngineObject obj,
                        Beutl.Composition.CompositionContext context,
                        ref bool updateOnly)
                    {
                        var typed = (HandwrittenResourceBase)obj;
                        if (!IsCompatibleUpdateOwner(typed))
                            throw new System.InvalidCastException();

                        using GeneratedResourceOperationLease operation =
                            BeginExclusiveResourceOperation(typed);
                        UpdateCore(typed, context, ref updateOnly);
                    }

                    protected virtual bool IsCompatibleUpdateOwner(HandwrittenResourceBase obj) => true;

                    protected virtual void UpdateCore(
                        HandwrittenResourceBase obj,
                        Beutl.Composition.CompositionContext context,
                        ref bool updateOnly)
                    {
                        base.Update(obj, context, ref updateOnly);
                    }
                }
            }

            public partial class GeneratedFromHandwrittenBase : HandwrittenResourceBase
            {
                public Beutl.Engine.IProperty<float> Marker { get; } =
                    Beutl.Engine.Property.Create(0f);
            }

            public sealed class PlainNonPartialFromHandwrittenBase : HandwrittenResourceBase
            {
            }
            """);
    }

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
    public void GeneratedResource_ExtendsSealedHandwrittenUpdateThroughTypedVirtualContract()
    {
        GeneratorHarnessResult result = RunTypedHandwrittenResourceInheritance();
        string source = result.GetSource("GeneratedFromHandwrittenBase_Resource.g.cs");

        Assert.Multiple(() =>
        {
            Assert.That(
                result.CompilationErrors,
                Is.Empty,
                "A generated derived Resource must compile over a sealed handwritten Update wrapper: "
                + string.Join(Environment.NewLine, result.CompilationErrors.Select(d => d.ToString())));
            Assert.That(source, Does.Contain(
                "protected override bool IsCompatibleUpdateOwner(global::SourceGeneratorTest.HandwrittenResourceBase obj)"));
            Assert.That(source, Does.Contain(
                "protected override void UpdateCore(global::SourceGeneratorTest.HandwrittenResourceBase obj"));
            Assert.That(source, Does.Contain("base.UpdateCore(obj, context, ref updateOnly);"));
            Assert.That(source, Does.Not.Contain("public override void Update("),
                "the sealed handwritten wrapper must remain the single owner preflight and exclusive-lease boundary");
        });
    }

    [Test]
    public void GeneratedResource_PreservesInheritedSuppressionForPlainNonPartialSubclass()
    {
        GeneratorHarnessResult result = RunTypedHandwrittenResourceInheritance();

        Assert.Multiple(() =>
        {
            Assert.That(result.HasSource("PlainNonPartialFromHandwrittenBase_Resource.g.cs"), Is.False);
            Assert.That(
                result.GeneratorDiagnostics.Where(diagnostic =>
                    diagnostic.GetMessage().Contains(
                        "PlainNonPartialFromHandwrittenBase",
                        StringComparison.Ordinal)),
                Is.Empty,
                "a legacy subclass with no generated resource state must keep using the compatible base Resource");
            Assert.That(result.CompilationErrors, Is.Empty,
                string.Join(Environment.NewLine, result.CompilationErrors.Select(d => d.ToString())));
        });
    }

    [Test]
    public void GeneratedResource_DoesNotBypassInheritedSuppressionWithoutTypedVirtualContract()
    {
        GeneratorHarnessResult result = GeneratorDriverHarness.Run(
            """
            namespace SourceGeneratorTest;

            [Beutl.Engine.SuppressResourceClassGeneration]
            public class NonExtensibleHandwrittenBase : Beutl.Engine.EngineObject
            {
                public new sealed class Resource : Beutl.Engine.EngineObject.Resource
                {
                }
            }

            public partial class DerivedFromNonExtensibleHandwrittenBase : NonExtensibleHandwrittenBase
            {
                public Beutl.Engine.IProperty<float> Marker { get; } =
                    Beutl.Engine.Property.Create(0f);
            }
            """);
        string source = result.GetSource("DerivedFromNonExtensibleHandwrittenBase_Resource.g.cs");

        Assert.Multiple(() =>
        {
            Assert.That(result.CompilationErrors, Is.Empty,
                string.Join(Environment.NewLine, result.CompilationErrors.Select(d => d.ToString())));
            Assert.That(source, Does.Contain("ScanPropertiesCore"));
            Assert.That(source, Does.Not.Contain("partial class Resource"));
            Assert.That(source, Does.Not.Contain("ToResource("));
        });
    }

    [Test]
    public void GeneratedResource_DoesNotBypassNearestShadowingResource()
    {
        GeneratorHarnessResult result = GeneratorDriverHarness.Run(
            """
            namespace SourceGeneratorTest;

            [Beutl.Engine.SuppressResourceClassGeneration]
            public class TypedHandwrittenBase : Beutl.Engine.EngineObject
            {
                public new class Resource : Beutl.Engine.EngineObject.Resource
                {
                    public sealed override void Update(
                        Beutl.Engine.EngineObject obj,
                        Beutl.Composition.CompositionContext context,
                        ref bool updateOnly)
                    {
                        var typed = (TypedHandwrittenBase)obj;
                        if (!IsCompatibleUpdateOwner(typed))
                            throw new System.InvalidCastException();

                        using GeneratedResourceOperationLease operation =
                            BeginExclusiveResourceOperation(typed);
                        UpdateCore(typed, context, ref updateOnly);
                    }

                    protected virtual bool IsCompatibleUpdateOwner(TypedHandwrittenBase obj) => true;

                    protected virtual void UpdateCore(
                        TypedHandwrittenBase obj,
                        Beutl.Composition.CompositionContext context,
                        ref bool updateOnly)
                    {
                        base.Update(obj, context, ref updateOnly);
                    }
                }
            }

            [Beutl.Engine.SuppressResourceClassGeneration]
            public class ShadowingHandwrittenOwner : TypedHandwrittenBase
            {
                public new sealed class Resource : TypedHandwrittenBase.Resource
                {
                }
            }

            public partial class GeneratedLeaf : ShadowingHandwrittenOwner
            {
                public Beutl.Engine.IProperty<float> Marker { get; } =
                    Beutl.Engine.Property.Create(0f);
            }
            """);
        string source = result.GetSource("GeneratedLeaf_Resource.g.cs");

        Assert.Multiple(() =>
        {
            Assert.That(result.CompilationErrors, Is.Empty,
                string.Join(Environment.NewLine, result.CompilationErrors.Select(d => d.ToString())));
            Assert.That(source, Does.Contain("ScanPropertiesCore"));
            Assert.That(source, Does.Not.Contain("partial class Resource"));
            Assert.That(source, Does.Not.Contain("ToResource("));
        });
    }

    [Test]
    public void GeneratedResource_RecognizesPendingPartialResourceInSameCompilation()
    {
        GeneratorHarnessResult result = GeneratorDriverHarness.Run(
            """
            namespace SourceGeneratorTest;

            [Beutl.Engine.SuppressResourceClassGeneration]
            public class TypedPartialBase : Beutl.Engine.EngineObject
            {
                public new class Resource : Beutl.Engine.EngineObject.Resource
                {
                    public sealed override void Update(
                        Beutl.Engine.EngineObject obj,
                        Beutl.Composition.CompositionContext context,
                        ref bool updateOnly)
                    {
                        var typed = (TypedPartialBase)obj;
                        if (!IsCompatibleUpdateOwner(typed))
                            throw new System.InvalidCastException();

                        using GeneratedResourceOperationLease operation =
                            BeginExclusiveResourceOperation(typed);
                        UpdateCore(typed, context, ref updateOnly);
                    }

                    protected virtual bool IsCompatibleUpdateOwner(TypedPartialBase obj) => true;

                    protected virtual void UpdateCore(
                        TypedPartialBase obj,
                        Beutl.Composition.CompositionContext context,
                        ref bool updateOnly)
                    {
                        base.Update(obj, context, ref updateOnly);
                    }
                }
            }

            public partial class GeneratedMiddle : TypedPartialBase
            {
                public Beutl.Engine.IProperty<float> MiddleMarker { get; } =
                    Beutl.Engine.Property.Create(0f);

                public partial class Resource
                {
                    public int UserExtensionMarker { get; set; }
                }
            }

            public partial class GeneratedLeafFromPartial : GeneratedMiddle
            {
                public Beutl.Engine.IProperty<float> LeafMarker { get; } =
                    Beutl.Engine.Property.Create(0f);
            }

            public partial class GeneratedSealedMiddle : TypedPartialBase
            {
                public sealed partial class Resource
                {
                }
            }

            public partial class GeneratedLeafFromSealedPartial : GeneratedSealedMiddle
            {
                public Beutl.Engine.IProperty<float> LeafMarker { get; } =
                    Beutl.Engine.Property.Create(0f);
            }
            """);
        string middleSource = result.GetSource("GeneratedMiddle_Resource.g.cs");
        string leafSource = result.GetSource("GeneratedLeafFromPartial_Resource.g.cs");
        string sealedLeafSource = result.GetSource("GeneratedLeafFromSealedPartial_Resource.g.cs");

        Assert.Multiple(() =>
        {
            Assert.That(result.CompilationErrors, Is.Empty,
                string.Join(Environment.NewLine, result.CompilationErrors.Select(d => d.ToString())));
            Assert.That(middleSource, Does.Contain(
                "protected override void UpdateCore(global::SourceGeneratorTest.TypedPartialBase obj"));
            Assert.That(leafSource, Does.Contain(
                "global::SourceGeneratorTest.GeneratedMiddle.Resource"));
            Assert.That(leafSource, Does.Contain(
                "protected override bool IsCompatibleUpdateOwner(global::SourceGeneratorTest.TypedPartialBase obj)"));
            Assert.That(leafSource, Does.Contain("public float LeafMarker"));
            Assert.That(leafSource, Does.Not.Contain("public override void Update("));
            Assert.That(sealedLeafSource, Does.Contain("ScanPropertiesCore"));
            Assert.That(sealedLeafSource, Does.Not.Contain("partial class Resource"));
            Assert.That(sealedLeafSource, Does.Not.Contain("ToResource("));
        });
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
    public void ConcreteType_ToResourceDisposesPartiallyInitializedResourceAndPreservesUpdateFailure()
    {
        string source = Run().GetSource("Derived_Resource.g.cs");

        int allocation = source.IndexOf("var resource = new global::SourceGeneratorTest.Derived.Resource();", StringComparison.Ordinal);
        int update = source.IndexOf("resource.Update(this, context, ref updateOnly);", allocation, StringComparison.Ordinal);
        int cleanup = source.IndexOf("resource.Dispose();", update, StringComparison.Ordinal);
        int rethrow = source.IndexOf("throw;", cleanup, StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(allocation, Is.GreaterThanOrEqualTo(0));
            Assert.That(update, Is.GreaterThan(allocation));
            Assert.That(cleanup, Is.GreaterThan(update));
            Assert.That(rethrow, Is.GreaterThan(cleanup));
        });
    }

    [Test]
    public void GeneratedResource_RejectsUpdateAndMutableAccessAfterDispose()
    {
        string valueSource = Run().GetSource("Derived_Resource.g.cs");
        string listSource = Run().GetSource("Derived3_Resource.g.cs");

        int updateMethod = valueSource.IndexOf("public override void Update", StringComparison.Ordinal);
        int updateReservation = valueSource.IndexOf(
            "var __resourceOperation = __BeginResourceOperation(__typedObject);",
            updateMethod,
            StringComparison.Ordinal);
        int preUpdate = valueSource.IndexOf("this.PreUpdate", updateMethod, StringComparison.Ordinal);
        int callbackGuard = valueSource.IndexOf(
            "global::System.ObjectDisposedException.ThrowIf(__IsResourceOperationInvalid(), this);",
            preUpdate,
            StringComparison.Ordinal);
        int valueSetter = valueSource.IndexOf("public float X", StringComparison.Ordinal);
        int valueSetterGuard = valueSource.IndexOf(
            "global::System.ObjectDisposedException.ThrowIf(__resourceCleanupCompleted || IsDisposed, this);",
            valueSetter,
            StringComparison.Ordinal);
        int listGetter = listSource.IndexOf(
            "public global::System.Collections.Generic.IReadOnlyList<global::SourceGeneratorTest.Derived.Resource> Items",
            StringComparison.Ordinal);
        int listGetterGuard = listSource.IndexOf(
            "global::System.ObjectDisposedException.ThrowIf(__resourceCleanupCompleted || IsDisposed, this);",
            listGetter,
            StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(updateReservation, Is.GreaterThan(updateMethod));
            Assert.That(updateReservation, Is.LessThan(preUpdate));
            Assert.That(callbackGuard, Is.GreaterThan(preUpdate));
            Assert.That(valueSetterGuard, Is.GreaterThan(valueSetter));
            Assert.That(listGetterGuard, Is.GreaterThan(listGetter));
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
            // Public initialization cannot overwrite an existing owner, and graph cleanup reserves the child
            // before any hook runs.
            Assert.That(source, Does.Contain("if (_child != null && !global::System.Object.ReferenceEquals(_child, value))"));
            Assert.That(source, Does.Contain("context.Reserve(_child);"));
            Assert.That(source, Does.Contain("context.DisposeOwned(_child);"));
            Assert.That(source, Does.Contain("_child = default!;"));
        });
    }

    [Test]
    public void Derived3_GeneratesListPropertyForIListPropertyMember()
    {
        string source = Run().GetSource("Derived3_Resource.g.cs");

        Assert.Multiple(() =>
        {
            // Items is exposed through an immutable snapshot while the generator retains exclusive ownership of
            // the mutable backing list.
            Assert.That(source, Does.Contain("public global::System.Collections.Generic.IReadOnlyList<global::SourceGeneratorTest.Derived.Resource> Items"));
            Assert.That(source, Does.Contain("CompareAndUpdateList(context"));
            Assert.That(source, Does.Contain("__RefreshItemsSnapshot();"));
            Assert.That(source, Does.Contain("context.Reserve(item);"));
            Assert.That(source, Does.Contain("context.DisposeOwned(item);"));
            Assert.That(source, Does.Contain("_items.Clear();"));
            Assert.That(source, Does.Contain("_itemsSnapshot = global::System.Array.Empty<global::SourceGeneratorTest.Derived.Resource>();"));
        });
    }

    [Test]
    public void GraphNodePort_ItemValueIsDetachedButNotDisposedByGeneratedResource()
    {
        const string nodeGraphStubs = """
            namespace Beutl.NodeGraph.Composition
            {
                public interface IItemValue { }

                public sealed class ItemValue<T> : IItemValue
                {
                    public T Value { get; set; } = default!;
                }
            }

            namespace Beutl.NodeGraph
            {
                public interface INodeMember { }

                [Beutl.Engine.SuppressResourceClassGeneration]
                public class NodeMember<T> : Beutl.Engine.EngineObject, INodeMember { }

                [Beutl.Engine.SuppressResourceClassGeneration]
                public class InputPort<T> : NodeMember<T> { }

                [Beutl.Engine.SuppressResourceClassGeneration]
                public class OutputPort<T> : NodeMember<T> { }

                public class GraphNode : Beutl.Engine.EngineObject
                {
                    public new class Resource : Beutl.Engine.EngineObject.Resource
                    {
                        public System.Collections.Generic.IReadOnlyDictionary<INodeMember, int> ItemIndexMap { get; }
                            = new System.Collections.Generic.Dictionary<INodeMember, int>();

                        public System.Collections.Generic.IReadOnlyList<Composition.IItemValue> ItemValues { get; }
                            = System.Array.Empty<Composition.IItemValue>();

                        public new GraphNode GetOriginal() => (GraphNode)base.GetOriginal();

                        protected virtual void BindNodePortValuesCore() { }
                    }
                }
            }

            namespace SourceGeneratorTest
            {
                public partial class GeneratedPortNode : Beutl.NodeGraph.GraphNode
                {
                    public Beutl.NodeGraph.OutputPort<int> Output { get; } = new();
                }
            }
            """;

        GeneratorHarnessResult result = GeneratorDriverHarness.Run(nodeGraphStubs);
        string source = result.GetSource("GeneratedPortNode_Resource.g.cs");

        Assert.Multiple(() =>
        {
            Assert.That(result.CompilationErrors, Is.Empty,
                string.Join(Environment.NewLine, result.CompilationErrors.Select(diagnostic => diagnostic.ToString())));
            Assert.That(source, Does.Contain("protected override void BindNodePortValuesCore()"));
            Assert.That(source, Does.Contain("_output_ItemValue = null;"));
            Assert.That(source, Does.Not.Contain("context.DisposeOwned(_output_ItemValue)"),
                "GraphSnapshot, not each generated node resource, owns port item values");
        });
    }

    [Test]
    public void Derived3_DisposeSweepsOwnedResourcesHooksAndBaseInOrder()
    {
        string source = Run().GetSource("Derived3_Resource.g.cs");

        int prepareOverride = source.IndexOf("protected override void PrepareGeneratedResourceCleanupCore(", StringComparison.Ordinal);
        int objectReserve = source.IndexOf("context.Reserve(_child);", StringComparison.Ordinal);
        int itemReserve = source.IndexOf("context.Reserve(item);", objectReserve, StringComparison.Ordinal);
        int preDispose = source.IndexOf("this.PreDispose(disposing);", itemReserve, StringComparison.Ordinal);
        int objectDispose = source.IndexOf("context.DisposeOwned(_child);", preDispose, StringComparison.Ordinal);
        int itemDispose = source.IndexOf("context.DisposeOwned(item);", objectDispose, StringComparison.Ordinal);
        int postDispose = source.IndexOf("this.PostDispose(disposing);", StringComparison.Ordinal);
        int objectDetach = source.IndexOf("_child = default!;", postDispose, StringComparison.Ordinal);
        int retainedListClear = source.IndexOf("_items.Clear();", objectDetach, StringComparison.Ordinal);
        int lifecycleSeal = source.IndexOf("__resourceCleanupCompleted = true;", postDispose, StringComparison.Ordinal);
        int localPrepare = source.IndexOf("__PrepareGeneratedResourceCleanup(disposing, context);", StringComparison.Ordinal);
        int basePrepare = source.IndexOf("base.PrepareGeneratedResourceCleanupCore(disposing, context);", StringComparison.Ordinal);
        int baseRollback = source.IndexOf("base.RollbackGeneratedResourceCleanupCore();", StringComparison.Ordinal);
        int localRollback = source.IndexOf("__RollbackGeneratedResourceCleanup();", baseRollback, StringComparison.Ordinal);
        int localCleanup = source.IndexOf("__CleanupGeneratedResource(disposing, context);", StringComparison.Ordinal);
        int baseCleanup = source.IndexOf("base.CleanupGeneratedResourceCore(disposing, context);", StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(prepareOverride, Is.GreaterThanOrEqualTo(0));
            Assert.That(objectReserve, Is.GreaterThan(prepareOverride));
            Assert.That(itemReserve, Is.GreaterThan(objectReserve));
            Assert.That(preDispose, Is.GreaterThan(itemReserve));
            Assert.That(objectDispose, Is.GreaterThan(preDispose));
            Assert.That(itemDispose, Is.GreaterThan(objectDispose));
            Assert.That(postDispose, Is.GreaterThan(itemDispose));
            Assert.That(objectDetach, Is.GreaterThan(postDispose));
            Assert.That(retainedListClear, Is.GreaterThan(objectDetach));
            Assert.That(lifecycleSeal, Is.GreaterThan(postDispose));
            Assert.That(basePrepare, Is.GreaterThan(localPrepare), "cleanup layers prepare from derived to base");
            Assert.That(localRollback, Is.GreaterThan(baseRollback), "rollback must reverse preparation order");
            Assert.That(baseCleanup, Is.GreaterThan(localCleanup), "cleanup must continue through every base layer");
            Assert.That(source, Does.Not.Contain("RegisterGeneratedResourceLifecycle"));
            Assert.That(source, Does.Not.Contain("protected override void Dispose(bool disposing)"));
        });
    }
}
