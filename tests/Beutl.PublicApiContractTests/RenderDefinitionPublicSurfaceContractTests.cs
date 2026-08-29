using System.Reflection;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;

namespace Beutl.PublicApiContractTests;

[TestFixture]
public sealed class RenderDefinitionPublicSurfaceContractTests
{
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

    [Test]
    public void EffectItemDescriptionsAndDescriptionOverloads_AreNotPublic()
    {
        string[] effectItemTypes =
        [
            "Beutl.Graphics.Rendering.OpaqueRenderDescription",
            "Beutl.Graphics.Rendering.TargetScopeDescription",
            "Beutl.Graphics.Rendering.TargetCommandDescription",
            "Beutl.Graphics.Rendering.RawTargetScopeDescription",
            "Beutl.Graphics.Rendering.RawTargetCommandDescription",
            "Beutl.Graphics.Effects.GeometryDescription",
            "Beutl.Graphics.Effects.ShaderDescription",
        ];
        Assembly engine = typeof(RenderNode).Assembly;
        string?[] exportedTypes = engine.GetExportedTypes().Select(static type => type.FullName).ToArray();

        Assert.Multiple(() =>
        {
            foreach (string effectItemType in effectItemTypes)
                Assert.That(exportedTypes, Does.Not.Contain(effectItemType), effectItemType);
        });
        AssertContextCallSurface(typeof(RenderNodeContext), "OpaqueSource", typeof(OpaqueRenderCall<>));
        AssertContextCallSurface(typeof(RenderNodeContext), "PaintedSource", typeof(PaintedSourceCall<>));
        AssertContextCallSurface(typeof(RenderNodeContext), "OpaqueMap", typeof(OpaqueRenderCall<>));
        AssertContextCallSurface(typeof(RenderNodeContext), "OpaqueCombine", typeof(OpaqueRenderCall<>));
        AssertContextCallSurface(typeof(RenderNodeContext), "OpaqueExpand", typeof(OpaqueRenderCall<>));
        AssertContextCallSurface(typeof(RenderNodeContext), "TargetScope", typeof(TargetScopeCall<>));
        AssertContextCallSurface(typeof(RenderNodeContext), "TargetCommand", typeof(TargetCommandCall<>));
        AssertContextCallSurface(typeof(RenderNodeContext), "RawTargetScope", typeof(RawTargetScopeCall<>));
        AssertContextCallSurface(typeof(RenderNodeContext), "RawTargetCommand", typeof(RawTargetCommandCall<>));
        AssertContextCallSurface(typeof(RenderNodeContext), "Geometry", typeof(GeometryCall<>));
        AssertContextCallSurface(typeof(RenderNodeContext), "Shader", typeof(ShaderCall<>));
        AssertContextCallSurface(typeof(FilterEffectContext), "Geometry", typeof(GeometryCall<>));
        AssertContextCallSurface(typeof(FilterEffectContext), "Shader", typeof(ShaderCall<>));
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

    private static void AssertContextCallSurface(Type context, string methodName, Type call)
    {
        MethodInfo[] methods = context
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(method => method.Name == methodName)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(methods, Has.Length.EqualTo(1), $"{context.Name}.{methodName}");
            if (methods.Length != 1)
                return;
            Assert.That(methods[0].IsGenericMethodDefinition, Is.True, $"{context.Name}.{methodName}");
            Assert.That(
                methods[0].GetParameters().Any(parameter =>
                    parameter.ParameterType.IsGenericType
                    && parameter.ParameterType.GetGenericTypeDefinition() == call),
                Is.True,
                $"{context.Name}.{methodName}");
        });
    }
}
