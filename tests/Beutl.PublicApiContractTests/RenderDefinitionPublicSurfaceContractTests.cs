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
    public void LegacyDescriptionsAndDescriptionOverloads_AreNotPublic()
    {
        string[] legacyTypes =
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
            foreach (string legacyType in legacyTypes)
                Assert.That(exportedTypes, Does.Not.Contain(legacyType), legacyType);
        });
        AssertContextCallSurface(typeof(RenderNodeContext), "OpaqueSource", typeof(OpaqueRenderCall<>));
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

    [Test]
    public void HasChanges_IsTheOnlyPublicNodeInvalidationSignal()
    {
        PropertyInfo? hasChanges = typeof(RenderNode).GetProperty(nameof(RenderNode.HasChanges));
        string[] excludedMembers = ["Cache", "CacheKey", "RuntimeIdentity", "ChangeVersion"];

        Assert.Multiple(() =>
        {
            Assert.That(hasChanges, Is.Not.Null);
            Assert.That(hasChanges!.CanRead, Is.True);
            Assert.That(hasChanges.CanWrite, Is.True);
            foreach (string member in excludedMembers)
                Assert.That(typeof(RenderNode).GetProperty(member), Is.Null, member);
            Assert.That(
                typeof(RenderNode).GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Any(static method => method.Name is "ClearCache" or "ResetCache" or "ReportRenderCount"),
                Is.False);
            Assert.That(typeof(RenderNodeContext).GetMethod("DisableRenderCache"), Is.Null);
        });
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
