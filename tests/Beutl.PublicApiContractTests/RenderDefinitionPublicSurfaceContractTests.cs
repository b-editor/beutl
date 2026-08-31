using System.Reflection;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;

namespace Beutl.PublicApiContractTests;

[TestFixture]
public sealed class RenderDefinitionPublicSurfaceContractTests
{
    private const BindingFlags AnyPublicMember =
        BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy;

    private static readonly string[] s_recordingNamespaces =
    [
        "Beutl.Graphics.Rendering",
        "Beutl.Graphics.Effects",
    ];

    [Test]
    public void DefinitionsAndCalls_AreTheExternalRecordingSurface()
    {
        AssertDefinitionCallSurface(typeof(OpaqueRenderDefinition<>), typeof(OpaqueRenderCall<>));
        AssertDefinitionCallSurface(typeof(TargetScopeDefinition<>), typeof(TargetScopeCall<>));
        AssertDefinitionCallSurface(typeof(TargetCommandDefinition<>), typeof(TargetCommandCall<>));
        AssertDefinitionCallSurface(typeof(RawTargetScopeDefinition<>), typeof(RawTargetScopeCall<>));
        AssertDefinitionCallSurface(typeof(RawTargetCommandDefinition<>), typeof(RawTargetCommandCall<>));
        AssertDefinitionCallSurface(typeof(GeometryDefinition<>), typeof(GeometryCall<>));
        AssertDefinitionCallSurface(typeof(ShaderDefinition<>), typeof(ShaderCall<>));
    }

    /// <remarks>
    /// This assertion is the inverse of the one it replaces. The seven descriptions used to be plan-internal,
    /// reachable only by going through a Definition and a Call, and this file asserted their absence from the
    /// exported set. They are now the recording surface itself, so their presence is what has to be pinned:
    /// a description that slips back to <see langword="internal"/> takes the whole family's authoring route
    /// with it.
    /// </remarks>
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

    /// <remarks>
    /// Two overloads per recording method is the honest count for as long as both routes exist: the
    /// Call-taking one an author reaches through a Definition, and the Description-taking one they reach
    /// directly. It is deliberately not "at least one" - a third overload appearing, or either of these two
    /// disappearing before the families are collapsed, is exactly what this is here to report.
    /// </remarks>
    [Test]
    public void EveryRecordingMethod_TakesBothACallAndADescription()
    {
        AssertContextRecordingSurface(
            typeof(RenderNodeContext), "OpaqueSource", typeof(OpaqueRenderCall<>), typeof(OpaqueRenderDescription));
        AssertContextRecordingSurface(
            typeof(RenderNodeContext), "OpaqueMap", typeof(OpaqueRenderCall<>), typeof(OpaqueRenderDescription));
        AssertContextRecordingSurface(
            typeof(RenderNodeContext), "OpaqueCombine", typeof(OpaqueRenderCall<>), typeof(OpaqueRenderDescription));
        AssertContextRecordingSurface(
            typeof(RenderNodeContext), "OpaqueExpand", typeof(OpaqueRenderCall<>), typeof(OpaqueRenderDescription));
        AssertContextRecordingSurface(
            typeof(RenderNodeContext), "TargetScope", typeof(TargetScopeCall<>), typeof(TargetScopeDescription));
        AssertContextRecordingSurface(
            typeof(RenderNodeContext), "TargetCommand", typeof(TargetCommandCall<>), typeof(TargetCommandDescription));
        AssertContextRecordingSurface(
            typeof(RenderNodeContext), "RawTargetScope", typeof(RawTargetScopeCall<>), typeof(RawTargetScopeDescription));
        AssertContextRecordingSurface(
            typeof(RenderNodeContext), "RawTargetCommand", typeof(RawTargetCommandCall<>), typeof(RawTargetCommandDescription));
        AssertContextRecordingSurface(
            typeof(RenderNodeContext), "Geometry", typeof(GeometryCall<>), typeof(GeometryDescription));
        AssertContextRecordingSurface(
            typeof(RenderNodeContext), "Shader", typeof(ShaderCall<>), typeof(ShaderDescription));
        AssertContextRecordingSurface(
            typeof(FilterEffectContext), "Geometry", typeof(GeometryCall<>), typeof(GeometryDescription));
        AssertContextRecordingSurface(
            typeof(FilterEffectContext), "Shader", typeof(ShaderCall<>), typeof(ShaderDescription));
    }

    /// <remarks>
    /// A painted source is the one family with no description to record, because two of the decisions its
    /// recording makes cannot be made anywhere else: the fill and the pen are borrowed against the active
    /// transaction, and whether either resolves to a brush that itself draws is what withdraws the
    /// direct-replay fast path. Both are record-time answers, so the context method is the description.
    /// Its resources therefore arrive as bindings: a bare token is bound to an engine slot no declaration
    /// names, which no <see cref="RenderHitTestContract.FromSlot{T}(RenderResourceSlot{T}, Func{T, Point, bool})"/>
    /// can resolve against.
    /// </remarks>
    [Test]
    public void PaintedSource_IsRecordableWithoutADefinition()
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
            Assert.That(methods, Has.Length.EqualTo(2), "one call-taking overload and one draw-taking overload");
            Assert.That(
                methods.Count(static method => method.GetParameters().Any(static parameter =>
                    parameter.ParameterType.IsGenericType
                    && parameter.ParameterType.GetGenericTypeDefinition() == typeof(PaintedSourceCall<>))),
                Is.EqualTo(1));
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

    /// <remarks>
    /// A description built from bindings alone has no slot list to check them against, so nothing there can
    /// report a caller that bound one slot twice and another not at all - the check a Call gets for free from
    /// the Definition that declares the slots. Every newly public factory takes that list back, which is also
    /// what restores the normalization: the bindings are reordered into declared-slot order before anything
    /// derived from them reaches a plan key.
    /// </remarks>
    [Test]
    public void EveryDescriptionFactory_AcceptsTheSlotsItsCallPathValidatesAgainst()
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

    /// <remarks>
    /// Publishing a description publishes only what an author declares. Everything the planner reads out of
    /// one - the fingerprint it keys a plan by, the execution channel it invokes, the engine-only factories
    /// that mint an identity no declaration could establish - stays behind, and the compiler cannot report a
    /// member that quietly stops doing so, because widening an internal member of a public type is legal.
    /// This is that report.
    /// </remarks>
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

    /// <remarks>
    /// The named half of the same rule, asked of types instead of members. Each of these is reachable from a
    /// description - a shader's bindings, its Vulkan lowering, the channel every retained callback is invoked
    /// through - and publishing a description must not drag any of them out with it.
    /// </remarks>
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

    /// <remarks>
    /// The roster read as the family's membership, so a Description exported without a line here is a type
    /// nobody checks. <see cref="MaterializedInputDescription"/> and <see cref="TargetCaptureDescription"/>
    /// are listed but hold no callback: they were already public before this family joined them, and they are
    /// the shape the seven were made to match.
    /// </remarks>
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

    /// <remarks>
    /// The behavioural half of the slot parameter, taken out-of-tree so it is exercised the way a plugin
    /// author reaches it. Ordering is asserted rather than only the throwing cases because it is the part a
    /// plan key depends on: <c>GeometryDescription</c> keys itself on the value types of its bindings in
    /// order, so a caller writing the same two bindings the other way round would otherwise compile a second
    /// plan for one operation.
    /// </remarks>
    [Test]
    public void ADescriptionGivenItsDeclaredSlots_ChecksAndNormalizesTheBindings()
    {
        RenderResourceSlot<SlotSubject> slotA = new();
        RenderResourceSlot<SlotSubject> slotB = new();
        var a = new SlotSubject();
        var b = new SlotSubject();
        RenderResourceBinding? bindA = null;
        RenderResourceBinding? bindB = null;
        IReadOnlyList<RenderResourceBinding> normalized = [];
        Exception? unbound = null;
        Exception? undeclared = null;
        Exception? boundTwice = null;

        using var node = new DelegateNode(context =>
        {
            bindA = slotA.Bind(context.Borrow(a));
            bindB = slotB.Bind(context.Borrow(b));

            normalized = Geometry(resources: [bindB, bindA], slots: [slotA, slotB]).Resources;
            unbound = Assert.Throws<ArgumentException>(
                () => Geometry(resources: [bindA], slots: [slotA, slotB]));
            undeclared = Assert.Throws<ArgumentException>(
                () => Geometry(resources: [bindA, bindB], slots: [slotA]));
            boundTwice = Assert.Throws<ArgumentException>(
                () => Geometry(resources: [bindA, bindA], slots: [slotA, slotB]));

            context.Publish(context.OpaqueSource(MetadataSource(new Rect(0, 0, 4, 4))));
        });

        using var renderer = CreateRenderer(node);
        renderer.Measure();

        Assert.Multiple(() =>
        {
            Assert.That(
                normalized,
                Is.EqualTo(new[] { bindA, bindB }),
                "the bindings are reordered into declared-slot order, so the order they were written in "
                + "cannot reach the recorded operation");
            Assert.That(unbound, Is.Not.Null);
            Assert.That(undeclared, Is.Not.Null);
            Assert.That(boundTwice, Is.Not.Null);
        });
    }

    /// <remarks>
    /// The same route without a slot list. Omitting <c>slots</c> declares none rather than leaving the check
    /// out, so the common case - no slots and no bindings - still records, while a binding whose slot was
    /// never declared is refused at record time. Nothing downstream could have applied the declared-order
    /// normalization to it, so it would have carried the order the caller happened to write it in into the
    /// plan key.
    /// </remarks>
    [Test]
    public void ADescriptionWithoutDeclaredSlots_BindsNothingAndRefusesAnUndeclaredBinding()
    {
        RenderResourceSlot<SlotSubject> slot = new();
        var subject = new SlotSubject();
        IReadOnlyList<RenderResourceBinding>? recorded = null;
        ArgumentException? undeclared = null;

        using var node = new DelegateNode(context =>
        {
            RenderResourceBinding binding = slot.Bind(context.Borrow(subject));

            recorded = Geometry(resources: [], slots: null).Resources;
            undeclared = Assert.Throws<ArgumentException>(
                () => Geometry(resources: [binding], slots: null));

            context.Publish(context.OpaqueSource(MetadataSource(new Rect(0, 0, 4, 4))));
        });

        using var renderer = CreateRenderer(node);
        renderer.Measure();

        Assert.Multiple(() =>
        {
            Assert.That(
                recorded,
                Is.Not.Null.And.Empty,
                "declaring no slots and binding nothing is the default, and it still records");
            Assert.That(
                undeclared?.ParamName,
                Is.EqualTo("slots"),
                "the refusal names the declaration the author left out, not the bindings they wrote");
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

    /// <remarks>
    /// The list above is read as the family's roster, so a Definition type added without a line there is a
    /// type nobody checks. This fails when the exported set stops matching, which is the only way a reader
    /// can trust the roster is complete. <see cref="PaintedSourceDefinition{TState}"/> is listed here but
    /// not above on purpose: its bounds are measured from the pen the call supplies, so its Call carries
    /// them rather than taking the uniform (state, bindings) shape the other seven share.
    /// </remarks>
    [Test]
    public void TheDefinitionFamily_HasNoMemberOutsideTheCheckedRoster()
    {
        string[] roster =
        [
            "Beutl.Graphics.Effects.GeometryDefinition`1",
            "Beutl.Graphics.Effects.ShaderDefinition`1",
            "Beutl.Graphics.Rendering.OpaqueRenderDefinition`1",
            "Beutl.Graphics.Rendering.PaintedSourceDefinition`1",
            "Beutl.Graphics.Rendering.RawTargetCommandDefinition`1",
            "Beutl.Graphics.Rendering.RawTargetScopeDefinition`1",
            "Beutl.Graphics.Rendering.TargetCommandDefinition`1",
            "Beutl.Graphics.Rendering.TargetScopeDefinition`1",
        ];

        string[] exported = typeof(RenderNode).Assembly
            .GetExportedTypes()
            .Where(static type => type.Name.EndsWith("Definition`1", StringComparison.Ordinal))
            .Select(static type => type.FullName!)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.That(
            exported,
            Is.EqualTo(roster),
            "A Definition type joined or left the public surface. Add it to DefinitionsAndCalls_"
            + "AreTheExternalRecordingSurface, give it the fixed-metadata remark the family carries, and "
            + "list it here.");
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

    private static GeometryDescription Geometry(
        IEnumerable<RenderResourceBinding> resources,
        IEnumerable<RenderResourceSlot>? slots)
        => GeometryDescription.Create(
            (byte)0,
            static (_, _) => { },
            RenderBoundsContract.Identity,
            RenderHitTestContract.OutputBounds,
            resources: resources,
            slots: slots);

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

    private static void AssertDefinitionCallSurface(Type definition, Type call)
    {
        MethodInfo? method = definition.GetMethod("Call", BindingFlags.Public | BindingFlags.Instance);

        Assert.Multiple(() =>
        {
            Assert.That(method, Is.Not.Null, definition.Name);
            if (method is null)
                return;
            Assert.That(method!.ReturnType.IsGenericType, Is.True, definition.Name);
            Assert.That(method.ReturnType.GetGenericTypeDefinition(), Is.EqualTo(call), definition.Name);
            Assert.That(method.GetParameters(), Has.Length.EqualTo(2), definition.Name);
            Assert.That(method.GetParameters()[1].ParameterType,
                Is.EqualTo(typeof(IEnumerable<RenderResourceBinding>)), definition.Name);
            Assert.That(method.GetParameters()[1].HasDefaultValue, Is.True, definition.Name);
        });
    }

    private static void AssertContextRecordingSurface(
        Type context,
        string methodName,
        Type call,
        Type description)
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
                Has.Length.EqualTo(2),
                $"{label} records through exactly one Call overload and one Description overload while both "
                + "routes exist");
            Assert.That(
                methods.Count(method =>
                    method.IsGenericMethodDefinition
                    && method.GetParameters().Any(parameter =>
                        parameter.ParameterType.IsGenericType
                        && parameter.ParameterType.GetGenericTypeDefinition() == call)),
                Is.EqualTo(1),
                label);
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
