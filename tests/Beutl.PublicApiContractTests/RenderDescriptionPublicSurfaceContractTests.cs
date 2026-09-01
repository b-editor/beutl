using System.Reflection;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.PublicApiContractTests;

[TestFixture]
public sealed class RenderDescriptionPublicSurfaceContractTests
{
    private const BindingFlags AnyPublicMember =
        BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy;

    private static readonly string[] s_recordingNamespaces =
    [
        "Beutl.Graphics.Rendering",
        "Beutl.Graphics.Effects",
    ];

    private const string CurrentPixelSource = "half4 apply(half4 color) { return color; }";

    private const string WholeSourceSource =
        "uniform shader src; half4 main(float2 coord) { return src.eval(coord); }";

    private static readonly Rect s_slotProbeBounds = new(0, 0, 4, 4);

    private static readonly string[] s_slotBindingFamilyNames =
    [
        "OpaqueRenderDescription.Create",
        "TargetScopeDescription.Create",
        "TargetCommandDescription.Create",
        "RawTargetScopeDescription.Create",
        "RawTargetCommandDescription.Create",
        "GeometryDescription.Create",
        "ShaderDescription.CurrentPixel",
        "ShaderDescription.WholeSource",
        "TargetCaptureDescription.Create",
        "MaterializedInputDescription.FromRenderTarget",
    ];

    /// <summary>Binds <paramref name="resources"/> against <paramref name="slots"/> through one factory.</summary>
    private delegate IReadOnlyList<RenderResourceBinding> BindSlots(
        IReadOnlyList<RenderResourceBinding> resources,
        IEnumerable<RenderResourceSlot>? slots);

    [Test]
    public void EffectItemDescriptions_AreTheExternalRecordingSurface()
    {
        Assembly engine = typeof(RenderNode).Assembly;
        string?[] exportedTypes = engine.GetExportedTypes().Select(static type => type.FullName).ToArray();

        Assert.Multiple(() =>
        {
            foreach (Type description in RecordedDescriptions())
            {
                Assert.That(exportedTypes, Does.Contain(description.FullName), description.Name);
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
                    && parameter.ParameterType == typeof(IEnumerable<RenderResourceBinding>)),
                Is.True,
                "a bare RenderResource cannot be addressed by a slot, so a declared hit test could never read it");
            Assert.That(
                parameters.Any(static parameter => parameter.ParameterType == typeof(IEnumerable<RenderResource>)),
                Is.False);
            Assert.That(
                parameters.Any(static parameter => parameter.Name == "directReplayAtExactIntegerReduction"),
                Is.False,
                "that knob names a planner fast path an out-of-tree node has no model of");
            Assert.That(
                parameters.Any(static parameter =>
                    parameter.Name == "slots"
                    && parameter.ParameterType == typeof(IEnumerable<RenderResourceSlot>)),
                Is.True);
        });
    }

    [Test]
    public void EveryDescriptionFactory_DeclaresTheSlotListParameter()
    {
        (Type Description, string Factory)[] factories =
        [
            (typeof(OpaqueRenderDescription), "Create"),
            (typeof(TargetScopeDescription), "Create"),
            (typeof(TargetCommandDescription), "Create"),
            (typeof(RawTargetScopeDescription), "Create"),
            (typeof(RawTargetCommandDescription), "Create"),
            (typeof(GeometryDescription), "Create"),
            (typeof(ShaderDescription), "CurrentPixel"),
            (typeof(ShaderDescription), "WholeSource"),
            (typeof(TargetCaptureDescription), "Create"),
            (typeof(MaterializedInputDescription), "FromRenderTarget"),
        ];

        Assert.Multiple(() =>
        {
            foreach ((Type description, string factory) in factories)
            {
                MethodInfo[] overloads = description
                    .GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .Where(method => method.Name == factory)
                    .ToArray();
                string label = $"{description.Name}.{factory}";

                Assert.That(overloads, Is.Not.Empty, label);
                foreach (MethodInfo overload in overloads)
                {
                    Assert.That(
                        overload.GetParameters().Any(static parameter =>
                            parameter.Name == "slots"
                            && parameter.ParameterType == typeof(IEnumerable<RenderResourceSlot>)),
                        Is.True,
                        label);
                }
            }

            // The retained-callback families take their state through a generic factory; the shader families
            // take theirs through the binding builder, so their factories are not generic.
            foreach (Type description in RecordedDescriptions().Where(static type => type != typeof(ShaderDescription)))
            {
                Assert.That(
                    description.GetMethods(BindingFlags.Public | BindingFlags.Static)
                        .Any(static method => method.Name == "Create" && method.IsGenericMethodDefinition),
                    Is.True,
                    description.Name);
            }
        });
    }

    [Test]
    public void ThePlanInternalsOfADescription_AreNotPublic()
    {
        (Type Description, string[] Members)[] planInternals =
        [
            (typeof(OpaqueRenderDescription),
            [
                "DefinitionFingerprint", "Execute", "GetStructuralIdentity", "ThrowIfIncompatible",
                "WithoutDirectReplay", "ResolveInputReadbacks", "BackendBoundary", "DirectReplay",
                "SupportsDirectDstOut", "HasDirectReplayMaterializationContract",
                "DirectReplayAtExactIntegerReduction", "CreateRequestLocal", "CreateCore",
                "CreateEngineSource", "CreateBackendBoundary",
            ]),
            (typeof(TargetScopeDescription),
            [
                "DefinitionFingerprint", "Execute", "IsValueReplayMap",
                "BuiltInBackdropCapturesBackingTarget", "CreateRequestLocal", "CreateValueReplayMap",
                "CreateCore",
            ]),
            (typeof(TargetCommandDescription),
            [
                "DefinitionFingerprint", "Execute", "ResolveInputReadbacks", "CreateRequestLocal", "CreateCore",
            ]),
            (typeof(RawTargetScopeDescription),
                ["DefinitionFingerprint", "Execute", "CreateRequestLocal", "CreateCore"]),
            (typeof(RawTargetCommandDescription),
                ["DefinitionFingerprint", "Execute", "CreateRequestLocal", "CreateCore"]),
            (typeof(GeometryDescription),
                ["DefinitionFingerprint", "Render", "StructuralIdentity", "CreateRequestLocal", "CreateCore"]),
            (typeof(ShaderDescription),
            [
                "Uniforms", "Resources", "SpirvLowering", "HasExecutionContextBinder", "CreateFragmentHitTest",
                "StructuralIdentity", "GetStructuralIdentity",
            ]),
        ];

        Assert.Multiple(() =>
        {
            foreach ((Type description, string[] members) in planInternals)
            {
                foreach (string member in members)
                {
                    Assert.That(
                        description.GetMember(member, AnyPublicMember),
                        Is.Empty,
                        $"{description.Name}.{member}");
                }
            }
        });
    }

    [Test]
    public void ThePlanInternalTypesADescriptionHolds_AreNotExported()
    {
        string[] planInternalTypes =
        [
            "Beutl.Graphics.Effects.ShaderUniformBinding",
            "Beutl.Graphics.Effects.ShaderResourceBinding",
            "Beutl.Graphics.Effects.SpirvShaderLowering",
            "Beutl.Graphics.Rendering.RenderExecutionChannel`1",
            "Beutl.Graphics.Rendering.RenderExecutionBinding`1",
            "Beutl.Graphics.Rendering.RenderBackendBoundary",
            "Beutl.Graphics.Rendering.EngineRenderResourceSlot",
            "Beutl.Graphics.Rendering.OpaqueRenderTopology",

            // The seventeen types the definition/call route was made of. They were the third and second
            // members of a three-type authoring surface the descriptions now hold on their own, so any of
            // them reappearing means that surface grew back.
            "Beutl.Graphics.Rendering.OpaqueRenderDefinition`1",
            "Beutl.Graphics.Rendering.OpaqueRenderCall`1",
            "Beutl.Graphics.Rendering.PaintedSourceDefinition`1",
            "Beutl.Graphics.Rendering.PaintedSourceCall`1",
            "Beutl.Graphics.Rendering.TargetScopeDefinition`1",
            "Beutl.Graphics.Rendering.TargetScopeCall`1",
            "Beutl.Graphics.Rendering.TargetCommandDefinition`1",
            "Beutl.Graphics.Rendering.TargetCommandCall`1",
            "Beutl.Graphics.Rendering.RawTargetScopeDefinition`1",
            "Beutl.Graphics.Rendering.RawTargetScopeCall`1",
            "Beutl.Graphics.Rendering.RawTargetCommandDefinition`1",
            "Beutl.Graphics.Rendering.RawTargetCommandCall`1",
            "Beutl.Graphics.Effects.GeometryDefinition`1",
            "Beutl.Graphics.Effects.GeometryCall`1",
            "Beutl.Graphics.Effects.ShaderDefinition`1",
            "Beutl.Graphics.Effects.ShaderCall`1",
            "Beutl.Graphics.Effects.ShaderDefinitionBuilder`1",
        ];
        string?[] exportedTypes = typeof(RenderNode).Assembly
            .GetExportedTypes()
            .Select(static type => type.FullName)
            .ToArray();

        Assert.Multiple(() =>
        {
            foreach (string planInternalType in planInternalTypes)
                Assert.That(exportedTypes, Does.Not.Contain(planInternalType), planInternalType);
        });
    }

    [Test]
    public void TheDescriptionFamily_HasNoMemberOutsideTheCheckedRoster()
    {
        string[] roster =
        [
            "Beutl.Graphics.Effects.GeometryDescription",
            "Beutl.Graphics.Effects.ShaderDescription",
            "Beutl.Graphics.Rendering.MaterializedInputDescription",
            "Beutl.Graphics.Rendering.OpaqueRenderDescription",
            "Beutl.Graphics.Rendering.RawTargetCommandDescription",
            "Beutl.Graphics.Rendering.RawTargetScopeDescription",
            "Beutl.Graphics.Rendering.TargetCaptureDescription",
            "Beutl.Graphics.Rendering.TargetCommandDescription",
            "Beutl.Graphics.Rendering.TargetScopeDescription",
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
            "A Description type joined or left the public surface. Add it to EffectItemDescriptions_"
            + "AreTheExternalRecordingSurface, give it the plan-internal roster the family carries, and list "
            + "it here.");
    }

    [Test]
    public void EveryDescriptionFactoryGivenItsDeclaredSlots_ChecksAndNormalizesTheBindings()
    {
        RenderResourceSlot<SlotSubject> slotA = new();
        RenderResourceSlot<SlotSubject> slotB = new();
        var a = new SlotSubject();
        var b = new SlotSubject();
        using RenderTarget target = RenderTarget.CreateNull(4, 4);
        RenderResourceBinding? bindA = null;
        RenderResourceBinding? bindB = null;
        List<(string Name, IReadOnlyList<RenderResourceBinding> Normalized, Exception? Unbound,
            Exception? Undeclared, Exception? BoundTwice)> observed = [];

        using var node = new DelegateNode(context =>
        {
            bindA = slotA.Bind(context.Borrow(a));
            bindB = slotB.Bind(context.Borrow(b));

            foreach ((string name, BindSlots bind) in SlotBindingFamilies(context.Borrow(target)))
            {
                observed.Add((
                    name,
                    bind([bindB, bindA], [slotA, slotB]),
                    Catch(() => bind([bindA], [slotA, slotB])),
                    Catch(() => bind([bindA, bindB], [slotA])),
                    Catch(() => bind([bindA, bindA], [slotA, slotB]))));
            }

            context.Publish(context.OpaqueSource(MetadataSource(new Rect(0, 0, 4, 4))));
        });

        using var renderer = CreateRenderer(node);
        renderer.Measure();

        Assert.That(
            observed.Select(static entry => entry.Name),
            Is.EqualTo(s_slotBindingFamilyNames),
            "every factory in the family answers this table, so one joining or leaving is reported here");
        Assert.Multiple(() =>
        {
            foreach ((string name, IReadOnlyList<RenderResourceBinding> normalized, Exception? unbound,
                Exception? undeclared, Exception? boundTwice) in observed)
            {
                Assert.That(
                    normalized,
                    Is.EqualTo(new[] { bindA, bindB }),
                    $"{name} must reorder its bindings into declared-slot order, so the order they were "
                    + "written in cannot reach the recorded operation");
                Assert.That(
                    unbound,
                    Is.InstanceOf<ArgumentException>(),
                    $"{name} must refuse a declared slot left unbound");
                Assert.That(
                    undeclared,
                    Is.InstanceOf<ArgumentException>(),
                    $"{name} must refuse a binding whose slot it did not declare");
                Assert.That(
                    boundTwice,
                    Is.InstanceOf<ArgumentException>(),
                    $"{name} must refuse one slot bound twice while another goes unbound");
            }
        });
    }

    [Test]
    public void EveryDescriptionFactoryWithoutDeclaredSlots_BindsNothingAndRefusesAnUndeclaredBinding()
    {
        RenderResourceSlot<SlotSubject> slot = new();
        var subject = new SlotSubject();
        using RenderTarget target = RenderTarget.CreateNull(4, 4);
        List<(string Name, IReadOnlyList<RenderResourceBinding> Recorded, Exception? Undeclared)> observed = [];

        using var node = new DelegateNode(context =>
        {
            RenderResourceBinding binding = slot.Bind(context.Borrow(subject));

            foreach ((string name, BindSlots bind) in SlotBindingFamilies(context.Borrow(target)))
                observed.Add((name, bind([], null), Catch(() => bind([binding], null))));

            context.Publish(context.OpaqueSource(MetadataSource(new Rect(0, 0, 4, 4))));
        });

        using var renderer = CreateRenderer(node);
        renderer.Measure();

        Assert.That(observed.Select(static entry => entry.Name), Is.EqualTo(s_slotBindingFamilyNames));
        Assert.Multiple(() =>
        {
            foreach ((string name, IReadOnlyList<RenderResourceBinding> recorded, Exception? undeclared) in observed)
            {
                Assert.That(
                    recorded,
                    Is.Empty,
                    $"{name} must still record when nothing is declared and nothing is bound");
                Assert.That(
                    (undeclared as ArgumentException)?.ParamName,
                    Is.EqualTo("slots"),
                    $"{name} must name the declaration the author left out, not the bindings they wrote");
            }
        });
    }

    // MarkChanged stays the only way to invalidate a cached node, and it only raises: HasChanges has no
    // setter and nothing public lowers it, so a node cannot withdraw a change it already reported.
    // DisableRenderCache is not a second invalidation signal: it opts a recording out of caching altogether,
    // which a node recording a child it does not list in ChildNodes has to be able to do for itself.
    [Test]
    public void MarkChanged_IsTheOnlyPublicNodeInvalidationSignal()
    {
        PropertyInfo? hasChanges = typeof(RenderNode).GetProperty(nameof(RenderNode.HasChanges));
        MethodInfo? markChanged = typeof(RenderNode).GetMethod(
            nameof(RenderNode.MarkChanged),
            BindingFlags.Public | BindingFlags.Instance);
        string[] excludedMembers = ["Cache", "CacheKey", "RuntimeIdentity", "ChangeVersion"];

        Assert.Multiple(() =>
        {
            Assert.That(hasChanges, Is.Not.Null);
            Assert.That(hasChanges!.CanRead, Is.True);
            Assert.That(
                hasChanges.CanWrite,
                Is.False,
                "a node able to lower its own flag would replay the recording the flag exists to replace");
            Assert.That(markChanged, Is.Not.Null);
            Assert.That(markChanged!.GetParameters(), Is.Empty);
            Assert.That(
                typeof(RenderNode).GetMethod("ClearChanges", BindingFlags.Public | BindingFlags.Instance),
                Is.Null,
                "lowering the flag belongs to the recording lifecycle, not to the node's public surface");
            foreach (string member in excludedMembers)
                Assert.That(typeof(RenderNode).GetProperty(member), Is.Null, member);
            Assert.That(
                typeof(RenderNode).GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Any(static method => method.Name is "ClearCache" or "ResetCache" or "ReportRenderCount"),
                Is.False);
        });
    }

    // The other half of that contract, asked of the nodes rather than of the base: a holder cannot move
    // state a node's Process reads. Each of these was a public auto-property, so an assignment from outside
    // left the node reporting no changes and its recording replayable over the new value. Update is now
    // their only writer, and it marks. BESG005 reports the shape but only as a warning, so this is the
    // check that fails a build which reopens one.
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

    /// <summary>Enumerates public factories that bind declared resource slots.</summary>
    /// <remarks>
    /// Shader descriptions expose bindings through <see cref="ShaderDescription.HitTestResources"/>.
    /// </remarks>
    private static IEnumerable<(string Name, BindSlots Bind)> SlotBindingFamilies(
        RenderResource<RenderTarget> materializedTarget)
    {
        yield return ("OpaqueRenderDescription.Create", static (resources, slots)
            => OpaqueRenderDescription.Create(
                (byte)0,
                static (_, _) => { },
                OpaqueRenderBoundsContract.Source(s_slotProbeBounds),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.Vector,
                resources: resources,
                slots: slots).Resources);

        yield return ("TargetScopeDescription.Create", static (resources, slots)
            => TargetScopeDescription.Create(
                (byte)0,
                static (_, _) => { },
                RenderBoundsContract.Identity,
                RenderHitTestContract.OutputBounds,
                RenderScaleContract.PreserveInputSupply,
                resources: resources,
                slots: slots).Resources);

        yield return ("TargetCommandDescription.Create", static (resources, slots)
            => TargetCommandDescription.Create(
                (byte)0,
                static (_, _) => { },
                TargetRegion.Full,
                s_slotProbeBounds,
                RenderHitTestContract.OutputBounds,
                resources: resources,
                slots: slots).Resources);

        yield return ("RawTargetScopeDescription.Create", static (resources, slots)
            => RawTargetScopeDescription.Create(
                (byte)0,
                static (_, _) => { },
                RenderBoundsContract.Identity,
                RenderHitTestContract.OutputBounds,
                RenderScaleContract.PreserveInputSupply,
                resources,
                slots).Resources);

        yield return ("RawTargetCommandDescription.Create", static (resources, slots)
            => RawTargetCommandDescription.Create(
                (byte)0,
                static (_, _) => { },
                s_slotProbeBounds,
                RenderHitTestContract.OutputBounds,
                resources,
                slots).Resources);

        yield return ("GeometryDescription.Create", static (resources, slots)
            => GeometryDescription.Create(
                (byte)0,
                static (_, _) => { },
                RenderBoundsContract.Identity,
                RenderHitTestContract.OutputBounds,
                resources: resources,
                slots: slots).Resources);

        yield return ("ShaderDescription.CurrentPixel", static (resources, slots)
            => ShaderDescription.CurrentPixel(
                CurrentPixelSource,
                hitTestResources: resources,
                slots: slots).HitTestResources);

        yield return ("ShaderDescription.WholeSource", static (resources, slots)
            => ShaderDescription.WholeSource(
                WholeSourceSource,
                RenderBoundsContract.Identity,
                hitTestResources: resources,
                slots: slots).HitTestResources);

        yield return ("TargetCaptureDescription.Create", static (resources, slots)
            => TargetCaptureDescription.Create(
                TargetRegion.Full,
                s_slotProbeBounds,
                RenderHitTestContract.OutputBounds,
                TargetCaptureScaleContract.MaterializeAtWorkingScale,
                resources,
                slots).Resources);

        yield return ("MaterializedInputDescription.FromRenderTarget", (resources, slots)
            => MaterializedInputDescription.FromRenderTarget(
                materializedTarget,
                s_slotProbeBounds,
                EffectiveScale.At(1),
                PixelRect.FromRect(s_slotProbeBounds, 1),
                default,
                RenderHitTestContract.OutputBounds,
                resources,
                slots).Resources);
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
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    Intent = RenderIntent.Preview,
                    OutputScale = 1,
                    MaxWorkingScale = 2,
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                },
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

    private sealed class DelegateNode(Action<RenderNodeContext> process) : RenderNode
    {
        public override void Process(RenderNodeContext context) => process(context);
    }
}
