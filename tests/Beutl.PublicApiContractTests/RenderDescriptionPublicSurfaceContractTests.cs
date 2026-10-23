using System.Reflection;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shaders;
using Beutl.Media;

namespace Beutl.PublicApiContractTests;

[TestFixture]
public sealed class RenderDescriptionPublicSurfaceContractTests
{
    private static readonly string[] s_recordingNamespaces =
    [
        "Beutl.Graphics.Rendering",
        "Beutl.Graphics.Effects",
        "Beutl.Graphics.Shaders",
    ];

    private const string CurrentPixelSource = "half4 apply(half4 color) { return color; }";

    private const string WholeSourceSource =
        "uniform shader src; half4 main(float2 coord) { return src.eval(coord); }";

    private static readonly Rect s_resourceProbeBounds = new(0, 0, 4, 4);

    private delegate IReadOnlyList<RenderResourceBinding> BindResources(
        IReadOnlyList<RenderResourceBinding> resources);

    [Test]
    public void RecordedDescriptions_AreTheExternalRecordingSurface()
    {
        Assert.Multiple(() =>
        {
            foreach (Type description in RecordedDescriptions())
            {
                Assert.That(description.IsSealed, Is.True, description.Name);
            }
        });
    }

    [Test]
    public void EveryRecordingMethod_TakesItsDescriptionAndNothingElse()
    {
        AssertContextRecordingSurface(
            typeof(RenderNodeContext), "OpaqueSource", typeof(OpaqueRenderDescription));
        AssertContextRecordingSurface(
            typeof(RenderNodeContext), "OpaqueMap", typeof(OpaqueRenderDescription));
        AssertContextRecordingSurface(
            typeof(RenderNodeContext), "OpaqueCombine", typeof(OpaqueRenderDescription));
        AssertContextRecordingSurface(
            typeof(RenderNodeContext), "OpaqueExpand", typeof(OpaqueRenderDescription));
        AssertContextRecordingSurface(
            typeof(RenderNodeContext), "TargetScope", typeof(TargetScopeDescription));
        AssertContextRecordingSurface(
            typeof(RenderNodeContext), "TargetCommand", typeof(TargetCommandDescription));
        AssertContextRecordingSurface(
            typeof(RenderNodeContext), "RawTargetScope", typeof(RawTargetScopeDescription));
        AssertContextRecordingSurface(
            typeof(RenderNodeContext), "RawTargetCommand", typeof(RawTargetCommandDescription));
        AssertContextRecordingSurface(
            typeof(RenderNodeContext), "Geometry", typeof(GeometryDescription));
        AssertContextRecordingSurface(
            typeof(RenderNodeContext), "Shader", typeof(ShaderDescription));
        AssertContextRecordingSurface(
            typeof(FilterEffectContext), "Geometry", typeof(GeometryDescription));
        AssertContextRecordingSurface(
            typeof(FilterEffectContext), "Shader", typeof(ShaderDescription));
    }

    [Test]
    public void PaintedSource_IsRecordedByOneDrawTakingOverload()
    {
        MethodInfo[] methods = typeof(RenderNodeContext)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(static method => method.Name == "PaintedSource")
            .ToArray();
        MethodInfo? flat = methods.FirstOrDefault(static method =>
            method.GetParameters().Any(static parameter =>
                parameter.ParameterType.IsGenericType
                && parameter.ParameterType.GetGenericTypeDefinition() == typeof(PaintedSourceDraw<>)));

        Assert.Multiple(() =>
        {
            Assert.That(methods, Has.Length.EqualTo(1), "one draw-taking overload and nothing beside it");
            Assert.That(flat, Is.Not.Null, "the draw-taking overload is the description-free recording route");
            if (flat is null)
                return;

            ParameterInfo[] parameters = flat.GetParameters();
            Assert.That(
                parameters.Any(static parameter =>
                    parameter.Name == "bindings"
                    && parameter.ParameterType == typeof(IReadOnlyList<RenderResourceBinding>)),
                Is.True,
                "a bare RenderResource cannot be addressed by a slot, so a declared hit test could never read it");
            Assert.That(
                parameters.Any(static parameter => parameter.ParameterType == typeof(IReadOnlyList<RenderResource>)),
                Is.False);
            Assert.That(
                parameters.Any(static parameter => parameter.Name == "directReplayAtExactIntegerReduction"),
                Is.False,
                "that knob names a planner fast path an out-of-tree node has no model of");
        });
    }

    [Test]
    public void DescriptionMembers_MatchThePublicRoster()
    {
        (Type Description, string[] Members)[] roster =
        [
            (typeof(OpaqueRenderDescription),
            [
                nameof(OpaqueRenderDescription.Bounds), nameof(OpaqueRenderDescription.HitTest),
                nameof(OpaqueRenderDescription.ValueCardinality), nameof(OpaqueRenderDescription.Scale),
                nameof(OpaqueRenderDescription.InputDemand), nameof(OpaqueRenderDescription.DeviceGridSensitivity),
                nameof(OpaqueRenderDescription.InputReadbacks), nameof(OpaqueRenderDescription.Resources),
                nameof(OpaqueRenderDescription.Create),
            ]),
            (typeof(TargetScopeDescription),
            [
                nameof(TargetScopeDescription.Bounds), nameof(TargetScopeDescription.HitTest),
                nameof(TargetScopeDescription.Scale), nameof(TargetScopeDescription.DeviceGridSensitivity),
                nameof(TargetScopeDescription.DeviceGridMapping), nameof(TargetScopeDescription.Resources),
                nameof(TargetScopeDescription.TransformSpace), nameof(TargetScopeDescription.Create),
            ]),
            (typeof(TargetCommandDescription),
            [
                nameof(TargetCommandDescription.InputDemand), nameof(TargetCommandDescription.AffectedRegion),
                nameof(TargetCommandDescription.QueryBounds), nameof(TargetCommandDescription.HitTest),
                nameof(TargetCommandDescription.Access), nameof(TargetCommandDescription.InputReadbacks),
                nameof(TargetCommandDescription.Resources), nameof(TargetCommandDescription.Create),
            ]),
            (typeof(RawTargetScopeDescription),
            [
                nameof(RawTargetScopeDescription.Bounds), nameof(RawTargetScopeDescription.HitTest),
                nameof(RawTargetScopeDescription.Scale), nameof(RawTargetScopeDescription.Resources),
                nameof(RawTargetScopeDescription.Create),
            ]),
            (typeof(RawTargetCommandDescription),
            [
                nameof(RawTargetCommandDescription.QueryBounds), nameof(RawTargetCommandDescription.HitTest),
                nameof(RawTargetCommandDescription.Resources), nameof(RawTargetCommandDescription.Create),
            ]),
            (typeof(GeometryDescription),
            [
                nameof(GeometryDescription.Bounds), nameof(GeometryDescription.InputDemand),
                nameof(GeometryDescription.HitTest), nameof(GeometryDescription.RequiresReadback),
                nameof(GeometryDescription.Resources), nameof(GeometryDescription.Create),
            ]),
            (typeof(ShaderDescription),
            [
                nameof(ShaderDescription.Kind), nameof(ShaderDescription.Source),
                nameof(ShaderDescription.Bounds), nameof(ShaderDescription.InputDemand),
                nameof(ShaderDescription.SourceTileMode), nameof(ShaderDescription.HitTest),
                nameof(ShaderDescription.HitTestResources), nameof(ShaderDescription.CurrentPixel),
                nameof(ShaderDescription.WholeSource),
            ]),
            (typeof(TargetCaptureDescription),
            [
                nameof(TargetCaptureDescription.SourceRegion), nameof(TargetCaptureDescription.Bounds),
                nameof(TargetCaptureDescription.HitTest), nameof(TargetCaptureDescription.Scale),
                nameof(TargetCaptureDescription.Resources), nameof(TargetCaptureDescription.Create),
            ]),
            (typeof(MaterializedInputDescription),
            [
                nameof(MaterializedInputDescription.Bounds), nameof(MaterializedInputDescription.EffectiveScale),
                nameof(MaterializedInputDescription.DeviceBounds), nameof(MaterializedInputDescription.DeviceGridOffset),
                nameof(MaterializedInputDescription.RasterBounds), nameof(MaterializedInputDescription.Resources),
                nameof(MaterializedInputDescription.FromRenderTarget),
            ]),
        ];

        foreach ((Type description, string[] members) in roster)
        {
            string[] actual = description
                .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(static member => member is not MethodInfo { IsSpecialName: true })
                .Select(static member => member.Name)
                .Distinct()
                .ToArray();
            Assert.That(actual, Is.EquivalentTo(members), description.Name);
        }
    }

    [Test]
    public void TheDescriptionFamily_HasNoMemberOutsideTheCheckedRoster()
    {
        string[] roster =
        [
            "Beutl.Graphics.Effects.GeometryDescription",
            "Beutl.Graphics.Rendering.MaterializedInputDescription",
            "Beutl.Graphics.Rendering.OpaqueRenderDescription",
            "Beutl.Graphics.Rendering.RawTargetCommandDescription",
            "Beutl.Graphics.Rendering.RawTargetScopeDescription",
            "Beutl.Graphics.Rendering.TargetCaptureDescription",
            "Beutl.Graphics.Rendering.TargetCommandDescription",
            "Beutl.Graphics.Rendering.TargetScopeDescription",
            "Beutl.Graphics.Shaders.ShaderDescription",
        ];

        string[] exported = typeof(RenderNode).Assembly
            .GetExportedTypes()
            .Where(static type =>
                type.Name.EndsWith("Description", StringComparison.Ordinal)
                && s_recordingNamespaces.Contains(type.Namespace))
            .Select(static type => type.FullName!)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.That(
            exported,
            Is.EqualTo(roster),
            "A Description type joined or left the public surface. Add it to RecordedDescriptions_"
            + "AreTheExternalRecordingSurface, give it a public-member roster, and list "
            + "it here.");
    }

    [Test]
    public void EveryDescriptionFactory_RejectsDuplicateAndDefaultBindings()
    {
        RenderResourceSlot<SlotSubject> slot = new();
        var subject = new SlotSubject();
        using RenderTarget target = RenderTarget.CreateNull(4, 4);
        List<(string Name, Exception? Duplicate, Exception? Uninitialized)> observed = [];

        using var node = new DelegateNode(context =>
        {
            RenderResourceBinding binding = slot.Bind(context.Borrow(subject));

            foreach ((string name, BindResources bind) in ResourceBindingFamilies(context.Borrow(target)))
            {
                observed.Add((
                    name,
                    Catch(() => bind([binding, binding])),
                    Catch(() => bind([default]))));
            }

            context.Publish(context.OpaqueSource(MetadataSource(new Rect(0, 0, 4, 4))));
        });

        using var renderer = CreateRenderer(node);
        renderer.Measure();

        Assert.Multiple(() =>
        {
            foreach ((string name, Exception? duplicate, Exception? uninitialized) in observed)
            {
                Assert.That(
                    duplicate,
                    Is.InstanceOf<ArgumentException>(),
                    $"{name} must refuse one typed slot bound more than once");
                Assert.That(
                    uninitialized,
                    Is.InstanceOf<ArgumentException>(),
                    $"{name} must refuse default(RenderResourceBinding)");
            }
        });
    }

    [Test]
    public void NodeInvalidation_HasAReadOnlyFlagAndParameterlessSignal()
    {
        PropertyInfo hasChanges = typeof(RenderNode).GetProperty(nameof(RenderNode.HasChanges))!;
        MethodInfo markChanged = typeof(RenderNode).GetMethod(nameof(RenderNode.MarkChanged))!;
        Assert.Multiple(() =>
        {
            Assert.That(hasChanges.CanWrite, Is.False);
            Assert.That(markChanged.GetParameters(), Is.Empty);
        });
    }

    // Process reads these, so only Update (which marks) may write them. BESG005 only warns, so this test is
    // what fails the build.
    [Test]
    public void BuiltInNodeStateThatProcessReads_IsNotAssignableFromOutsideTheNode()
    {
        (Type Node, string Property)[] state =
        [
            (typeof(RectangleRenderNode), nameof(RectangleRenderNode.Rect)),
            (typeof(OpacityMaskRenderNode), nameof(OpacityMaskRenderNode.Mask)),
            (typeof(OpacityMaskRenderNode), nameof(OpacityMaskRenderNode.MaskBounds)),
            (typeof(OpacityMaskRenderNode), nameof(OpacityMaskRenderNode.Invert)),
        ];

        Assert.Multiple(() =>
        {
            foreach ((Type node, string property) in state)
            {
                PropertyInfo? declared = node.GetProperty(property);
                Assert.That(declared, Is.Not.Null, $"{node.Name}.{property}");
                if (declared is null)
                    continue;
                Assert.That(declared.CanRead, Is.True, $"{node.Name}.{property}");
                Assert.That(
                    declared.GetSetMethod(nonPublic: false),
                    Is.Null,
                    $"{node.Name}.{property} is state Process reads, so assigning it from outside would "
                    + "leave the node reporting no changes");
            }

            foreach (Type node in state.Select(static entry => entry.Node).Distinct())
            {
                Assert.That(
                    node.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .Any(static method => method.Name == "Update"),
                    Is.True,
                    $"{node.Name} must keep the Update overload that moves the value and marks");
            }
        });
    }

    [Test]
    public void NoDefinitionOrCallTypeIsExported()
    {
        string[] exported = typeof(RenderNode).Assembly
            .GetExportedTypes()
            .Where(static type =>
                s_recordingNamespaces.Contains(type.Namespace)
                && (type.Name.EndsWith("Definition`1", StringComparison.Ordinal)
                    || type.Name.EndsWith("Call`1", StringComparison.Ordinal)))
            .Select(static type => type.FullName!)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.That(
            exported,
            Is.Empty,
            "A Definition or Call type is exported again. One description per operation is the whole "
            + "authoring surface; a reusable shape an author binds state to separately is the three-type "
            + "surface this replaced.");
    }

    [Test]
    public void DisableRenderCache_IsReachableByAnOutOfTreeNode()
    {
        MethodInfo? optOut = typeof(RenderNodeContext).GetMethod(
            "DisableRenderCache",
            BindingFlags.Public | BindingFlags.Instance);

        Assert.That(optOut, Is.Not.Null,
            "a node that records an unlisted child must be able to keep itself out of the cache");
    }

    private static IEnumerable<Type> RecordedDescriptions()
    {
        yield return typeof(OpaqueRenderDescription);
        yield return typeof(TargetScopeDescription);
        yield return typeof(TargetCommandDescription);
        yield return typeof(RawTargetScopeDescription);
        yield return typeof(RawTargetCommandDescription);
        yield return typeof(GeometryDescription);
        yield return typeof(ShaderDescription);
    }

    /// <summary>Enumerates public factories that retain resource bindings.</summary>
    /// <remarks>
    /// Shader descriptions expose bindings through <see cref="ShaderDescription.HitTestResources"/>.
    /// </remarks>
    private static IEnumerable<(string Name, BindResources Bind)> ResourceBindingFamilies(
        RenderResource<RenderTarget> materializedTarget)
    {
        yield return ("OpaqueRenderDescription.Create", static resources
            => OpaqueRenderDescription.Create(
                (byte)0,
                static (_, _) => { },
                OpaqueRenderBoundsContract.Source(s_resourceProbeBounds),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.Vector,
                resources: resources).Resources);

        yield return ("TargetScopeDescription.Create", static resources
            => TargetScopeDescription.Create(
                (byte)0,
                static (_, _) => { },
                RenderBoundsContract.Identity,
                RenderHitTestContract.OutputBounds,
                RenderScaleContract.PreserveInputSupply,
                resources: resources).Resources);

        yield return ("TargetCommandDescription.Create", static resources
            => TargetCommandDescription.Create(
                (byte)0,
                static (_, _) => { },
                TargetRegion.Full,
                s_resourceProbeBounds,
                RenderHitTestContract.OutputBounds,
                resources: resources).Resources);

        yield return ("RawTargetScopeDescription.Create", static resources
            => RawTargetScopeDescription.Create(
                (byte)0,
                static (_, _) => { },
                RenderBoundsContract.Identity,
                RenderHitTestContract.OutputBounds,
                RenderScaleContract.PreserveInputSupply,
                resources).Resources);

        yield return ("RawTargetCommandDescription.Create", static resources
            => RawTargetCommandDescription.Create(
                (byte)0,
                static (_, _) => { },
                s_resourceProbeBounds,
                RenderHitTestContract.OutputBounds,
                resources).Resources);

        yield return ("GeometryDescription.Create", static resources
            => GeometryDescription.Create(
                (byte)0,
                static (_, _) => { },
                RenderBoundsContract.Identity,
                RenderHitTestContract.OutputBounds,
                resources: resources).Resources);

        yield return ("ShaderDescription.CurrentPixel", static resources
            => ShaderDescription.CurrentPixel(
                CurrentPixelSource,
                hitTestResources: resources).HitTestResources);

        yield return ("ShaderDescription.WholeSource", static resources
            => ShaderDescription.WholeSource(
                WholeSourceSource,
                RenderBoundsContract.Identity,
                hitTestResources: resources).HitTestResources);

        yield return ("TargetCaptureDescription.Create", static resources
            => TargetCaptureDescription.Create(
                TargetRegion.Full,
                s_resourceProbeBounds,
                RenderHitTestContract.OutputBounds,
                TargetCaptureScaleContract.MaterializeAtWorkingScale,
                resources).Resources);

        yield return ("MaterializedInputDescription.FromRenderTarget", resources
            => MaterializedInputDescription.FromRenderTarget(
                materializedTarget,
                s_resourceProbeBounds,
                EffectiveScale.At(1),
                PixelRect.FromRect(s_resourceProbeBounds, 1),
                default,
                RenderHitTestContract.OutputBounds,
                resources).Resources);
    }

    private static Exception? Catch(Func<IReadOnlyList<RenderResourceBinding>> bind)
    {
        try
        {
            bind();
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private static OpaqueRenderDescription MetadataSource(Rect bounds)
        => OpaqueRenderDescription.Create(
            (byte)0,
            static (_, _) => throw new AssertionException("A metadata request must not execute the source."),
            OpaqueRenderBoundsContract.Source(bounds),
            RenderHitTestContract.OutputBounds,
            RenderValueCardinality.Single,
            RenderScaleContract.Vector);

    private static RenderNodeRenderer CreateRenderer(RenderNode node)
        => new(
            node,
            new RenderNodeRenderRequest
            {
                Intent = RenderIntent.Preview,
                OutputScale = 1,
                MaxWorkingScale = 2,
                CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
            });

    private static void AssertContextRecordingSurface(Type context, string methodName, Type description)
    {
        MethodInfo[] methods = context
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(method => method.Name == methodName)
            .ToArray();
        string label = $"{context.Name}.{methodName}";

        Assert.Multiple(() =>
        {
            Assert.That(
                methods,
                Has.Length.EqualTo(1),
                $"{label} records through exactly one overload, and it takes the description");
            Assert.That(
                methods.Count(method =>
                    !method.IsGenericMethodDefinition
                    && method.GetParameters().Any(parameter => parameter.ParameterType == description)),
                Is.EqualTo(1),
                label);
        });
    }

    private sealed class SlotSubject;
}
