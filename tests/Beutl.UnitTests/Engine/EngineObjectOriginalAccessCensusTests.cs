using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Beutl.UnitTests.Engine;

/// <summary>
/// Pins every <c>GetOriginal()</c> call site under <c>src/</c>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Beutl.Engine.EngineObject.Resource.GetOriginal"/> is declared non-nullable but returns null for a
/// resource that never went through <c>ToResource</c>, so each call site is a place where a detached resource
/// either has to be handled or provably cannot arrive. Prose cannot carry that list: the enumeration in the
/// commit that introduced <c>RequireOriginal()</c> was written from a <c>GetOriginal().Member</c> search and
/// therefore missed <c>GraphicsContext2D.DrawDrawable</c>, which spells the same dereference across two
/// statements. This census is the machine-checked replacement — it counts invocations syntactically, so the
/// two-statement form is counted like any other, and adding a call site fails until the baseline is updated
/// deliberately. Counting syntactically also means <c>ResourceClassEmitter</c>, which writes the call into
/// generated source as a string literal, is correctly absent: it emits a call site rather than being one.
/// </para>
/// <para>
/// A new entry here is not by itself a defect. It is a prompt to decide, at that site, what a detached resource
/// should do: handle it, route the identity through <c>EngineResourceIdentity.Of</c>, or call
/// <c>RequireOriginal()</c> so the failure names the type instead of throwing <see cref="NullReferenceException"/>.
/// </para>
/// </remarks>
[TestFixture]
public sealed class EngineObjectOriginalAccessCensusTests
{
    private static readonly IReadOnlyDictionary<string, int> s_baseline =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["src/Beutl.Editor.Components/PathEditorTab/Views/PathGeometryControl.cs"] = 2,
            ["src/Beutl.Engine/Audio/Composing/Composer.cs"] = 1,
            ["src/Beutl.Engine/Audio/SoundGroup.cs"] = 2,
            ["src/Beutl.Engine/Engine/EngineObject.cs"] = 2,
            ["src/Beutl.Engine/Engine/ResourceExtension.cs"] = 2,
            ["src/Beutl.Engine/Engine/ResourceReconciler.cs"] = 2,
            ["src/Beutl.Engine/Graphics/AudioVisualizers/AudioVisualizerDrawable.cs"] = 1,
            ["src/Beutl.Engine/Graphics/DrawableTimeController.cs"] = 1,
            ["src/Beutl.Engine/Graphics/FilterEffects/DelayAnimationEffect.cs"] = 3,
            ["src/Beutl.Engine/Graphics/FilterEffects/FilterEffectGroup.cs"] = 1,
            ["src/Beutl.Engine/Graphics/FilterEffects/FilterEffectPresenter.cs"] = 1,
            ["src/Beutl.Engine/Graphics/ImmediateCanvas.cs"] = 1,
            ["src/Beutl.Engine/Graphics/Rendering/EngineResourceIdentity.cs"] = 1,
            ["src/Beutl.Engine/Graphics/Rendering/FilterEffectRenderNode.cs"] = 1,
            ["src/Beutl.Engine/Graphics/Rendering/Renderer.cs"] = 3,
            ["src/Beutl.Engine/Graphics/Shapes/EllipseShape.cs"] = 1,
            ["src/Beutl.Engine/Graphics/Shapes/RectShape.cs"] = 1,
            ["src/Beutl.Engine/Graphics/Shapes/RoundedRectShape.cs"] = 1,
            ["src/Beutl.Engine/Graphics3D/Primitives/Cube3D.cs"] = 1,
            ["src/Beutl.Engine/Graphics3D/Primitives/Plane3D.cs"] = 1,
            ["src/Beutl.Engine/Graphics3D/Primitives/Sphere3D.cs"] = 1,
            ["src/Beutl.Engine/Graphics3D/Scene3DRenderNode.cs"] = 1,
            ["src/Beutl.NodeGraph/Composition/GraphSnapshot.cs"] = 4,
            ["src/Beutl.NodeGraph/Nodes/GeometryShapeNode.cs"] = 3,
            ["src/Beutl.ProjectSystem/ProjectSystem/SceneDrawable.cs"] = 2,
            ["src/Beutl/Views/PlayerView.axaml.MouseControl.cs"] = 4,
        };

    [Test]
    public void EveryGetOriginalCallSiteUnderSrc_IsAccountedForInTheBaseline()
    {
        IReadOnlyDictionary<string, int> actual = CountGetOriginalCalls();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                actual.Keys.Order(StringComparer.Ordinal),
                Is.EqualTo(s_baseline.Keys.Order(StringComparer.Ordinal)).AsCollection,
                "a file gained or lost GetOriginal() calls; decide what a detached resource does there, "
                + "then update the baseline");
            foreach ((string path, int expected) in s_baseline)
            {
                actual.TryGetValue(path, out int found);
                Assert.That(found, Is.EqualTo(expected), path);
            }
        }
    }

    [Test]
    public void NoCallSiteUnderSrc_SilencesTheNullabilityWithTheNullForgivingOperator()
    {
        List<string> offenders = [];
        foreach ((string relativePath, CompilationUnitSyntax root) in EnumerateSources())
        {
            foreach (PostfixUnaryExpressionSyntax suppression in root.DescendantNodes()
                         .OfType<PostfixUnaryExpressionSyntax>()
                         .Where(static node => node.IsKind(SyntaxKind.SuppressNullableWarningExpression)))
            {
                if (suppression.Operand is InvocationExpressionSyntax invocation && IsGetOriginal(invocation))
                    offenders.Add($"{relativePath}:{LineOf(suppression)}");
            }
        }

        Assert.That(offenders, Is.Empty,
            "GetOriginal()! asserts a non-null original rather than deciding what a detached resource does; "
            + "call RequireOriginal() so the failure names the type, or handle the null");
    }



    private static IReadOnlyDictionary<string, int> CountGetOriginalCalls()
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach ((string relativePath, CompilationUnitSyntax root) in EnumerateSources())
        {
            int count = root.DescendantNodes().OfType<InvocationExpressionSyntax>().Count(IsGetOriginal);
            if (count > 0)
                counts[relativePath] = count;
        }

        return counts;
    }

    private static bool IsGetOriginal(InvocationExpressionSyntax invocation)
    {
        if (invocation.ArgumentList.Arguments.Count != 0)
            return false;

        return invocation.Expression switch
        {
            MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText == "GetOriginal",
            MemberBindingExpressionSyntax binding => binding.Name.Identifier.ValueText == "GetOriginal",
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText == "GetOriginal",
            _ => false,
        };
    }

    private static int LineOf(SyntaxNode node)
        => node.SyntaxTree.GetLineSpan(node.Span).StartLinePosition.Line + 1;

    private static IEnumerable<(string RelativePath, CompilationUnitSyntax Root)> EnumerateSources()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Beutl.slnx")))
            directory = directory.Parent;

        if (directory is null)
        {
            throw new DirectoryNotFoundException(
                $"Could not locate the Beutl repository root above {AppContext.BaseDirectory}.");
        }

        string root = directory.FullName;
        foreach (string path in Directory
                     .EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
                     .Order(StringComparer.Ordinal))
        {
            string relativePath = Path.GetRelativePath(root, path).Replace('\\', '/');
            string[] segments = relativePath.Split('/');
            if (segments.Contains("bin") || segments.Contains("obj"))
                continue;

            yield return (relativePath, CSharpSyntaxTree
                .ParseText(File.ReadAllText(path), path: relativePath)
                .GetCompilationUnitRoot());
        }
    }
}
