using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Recording;

[TestFixture]
public sealed class RecordingSideEffectTests
{
    private static readonly string[] s_forbiddenEagerInvocations =
    [
        "Acquire",
        "CreateRenderTarget",
        "CreateSkiaSurface",
        "Decode",
        "Flush",
        "GetFrame",
        "GetRenderTarget",
        "Initialize",
        "Pull",
        "PullToRoot",
        "Rasterize",
        "Read",
        "ReadAudio",
        "ReadFrame",
        "ReadPixels",
        "ReadVideo",
        "Render",
        "RenderDrawableToTarget",
        "RenderFallbackEllipse",
        "Resize",
        "Submit",
        "Synchronize",
        "UseSnapshot",
        "Wait",
    ];

    private static readonly string[] s_forbiddenEagerConstructions =
    [
        "ImmediateCanvas",
        "Renderer",
        "Renderer3D",
        "RenderNodeProcessor",
        "RenderNodeRenderer",
        "RenderTarget",
    ];

    [Test]
    public void EveryProductionProcessOverride_DefersGpuMediaAndNestedExecution()
    {
        string repositoryRoot = FindRepositoryRoot();
        SourceMethod[] overrides = EnumerateProductionProcessOverrides(repositoryRoot).ToArray();
        SourceFinding[] findings = overrides
            .SelectMany(FindEagerExecution)
            .OrderBy(static finding => finding.RelativePath, StringComparer.Ordinal)
            .ThenBy(static finding => finding.Line)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(overrides, Has.Length.EqualTo(30),
                "The recording probe must cover the 28 surviving baseline overrides and both new request facades.");
            Assert.That(findings, Is.Empty,
                "RenderNode.Process must only capture immutable CPU state and descriptions; execution belongs in "
                + $"deferred callbacks.{Environment.NewLine}{FormatFindings(findings)}");
        });
    }

    [Test]
    public void DescriptionConstruction_DefersEveryExecutionCapability()
    {
        var tripwire = new SideEffectTripwire();
        var bounds = new Rect(0, 0, 64, 36);

        OpaqueRenderDescription opaque = OpaqueRenderDescription.Create(
            _ => tripwire.TouchAll(),
            RenderOperationBoundsContract.Source(bounds),
            RenderHitTestContract.OutputBounds,
            RenderValueCardinality.Single,
            RenderScaleContract.Vector,
            structuralKey: "opaque-recording-probe",
            requiresReadback: true);
        TargetCommandDescription command = TargetCommandDescription.Create(
            _ => tripwire.TouchAll(),
            TargetRegion.Full,
            bounds,
            RenderHitTestContract.OutputBounds,
            TargetAccess.Readback,
            requiresInputReadback: true,
            structuralKey: "target-command-recording-probe");
        TargetScopeDescription scope = TargetScopeDescription.Create(
            _ => tripwire.TouchAll(),
            RenderBoundsContract.Identity,
            RenderHitTestContract.AnyInput,
            RenderScaleContract.PreserveInputSupply,
            structuralKey: "target-scope-recording-probe");
        RawTargetScopeDescription rawScope = RawTargetScopeDescription.Create(
            _ => tripwire.TouchAll(),
            RenderBoundsContract.Identity,
            RenderHitTestContract.AnyInput,
            RenderScaleContract.PreserveInputSupply,
            structuralKey: "raw-target-scope-recording-probe");
        RawTargetCommandDescription rawCommand = RawTargetCommandDescription.Create(
            _ => tripwire.TouchAll(),
            bounds,
            RenderHitTestContract.OutputBounds,
            structuralKey: "raw-target-command-recording-probe");

        Assert.Multiple(() =>
        {
            Assert.That(opaque, Is.Not.Null);
            Assert.That(command, Is.Not.Null);
            Assert.That(scope, Is.Not.Null);
            Assert.That(rawScope, Is.Not.Null);
            Assert.That(rawCommand, Is.Not.Null);
            Assert.That(tripwire.Counts.Values, Is.All.Zero,
                "Description creation/recording must not execute a deferred callback.");
        });
    }

    private static IEnumerable<SourceMethod> EnumerateProductionProcessOverrides(string repositoryRoot)
    {
        string sourceRoot = Path.Combine(repositoryRoot, "src");
        foreach (string path in Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories))
        {
            string relativePath = NormalizePath(Path.GetRelativePath(repositoryRoot, path));
            if (HasPathSegment(relativePath, "bin") || HasPathSegment(relativePath, "obj"))
                continue;

            SourceText text = SourceText.From(File.ReadAllText(path));
            CompilationUnitSyntax root = CSharpSyntaxTree.ParseText(
                    text,
                    CSharpParseOptions.Default.WithDocumentationMode(DocumentationMode.Parse),
                    relativePath)
                .GetCompilationUnitRoot();
            foreach (MethodDeclarationSyntax method in root.DescendantNodes()
                         .OfType<MethodDeclarationSyntax>()
                         .Where(IsRenderNodeProcessOverride))
            {
                yield return new SourceMethod(relativePath, text, method);
            }
        }
    }

    private static IEnumerable<SourceFinding> FindEagerExecution(SourceMethod source)
    {
        var invocationNames = new HashSet<string>(s_forbiddenEagerInvocations, StringComparer.Ordinal);
        var constructionNames = new HashSet<string>(s_forbiddenEagerConstructions, StringComparer.Ordinal);
        IEnumerable<SyntaxNode> eagerNodes = source.Method.DescendantNodes(
            descendIntoChildren: static node =>
                node is not AnonymousFunctionExpressionSyntax
                && node is not LocalFunctionStatementSyntax);

        foreach (InvocationExpressionSyntax invocation in eagerNodes.OfType<InvocationExpressionSyntax>())
        {
            string? name = GetInvokedName(invocation);
            if (name is not null
                && invocationNames.Contains(name)
                && !IsCpuNodeDescriptionRender(invocation, name))
            {
                yield return source.ToFinding(
                    invocation,
                    $"eager invocation '{name}'");
            }

            if (name == "Snapshot" && IsNativeSnapshotReceiver(invocation.Expression))
            {
                yield return source.ToFinding(invocation, "eager surface/target snapshot");
            }

            if ((name is null || !invocationNames.Contains(name))
                && IsTargetFactoryInvocation(invocation.Expression))
            {
                yield return source.ToFinding(invocation, "eager target-factory access");
            }
        }

        foreach (ObjectCreationExpressionSyntax creation in eagerNodes.OfType<ObjectCreationExpressionSyntax>())
        {
            string? name = creation.Type.DescendantTokens()
                .LastOrDefault(static token => token.IsKind(SyntaxKind.IdentifierToken))
                .ValueText;
            if (name is not null && constructionNames.Contains(name))
            {
                yield return source.ToFinding(
                    creation,
                    $"eager construction '{name}'");
            }
        }

        foreach (IdentifierNameSyntax identifier in eagerNodes.OfType<IdentifierNameSyntax>())
        {
            if (identifier.Identifier.ValueText == "GraphicsContextFactory")
            {
                yield return source.ToFinding(identifier, "eager GPU-context access");
            }
        }
    }

    private static bool IsRenderNodeProcessOverride(MethodDeclarationSyntax method)
    {
        if (method.Identifier.ValueText != "Process"
            || !method.Modifiers.Any(SyntaxKind.OverrideKeyword)
            || method.ParameterList.Parameters.Count != 1)
        {
            return false;
        }

        TypeSyntax? type = method.ParameterList.Parameters[0].Type;
        return type?.DescendantTokens()
            .LastOrDefault(static token => token.IsKind(SyntaxKind.IdentifierToken))
            .ValueText == "RenderNodeContext";
    }

    private static string? GetInvokedName(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            GenericNameSyntax generic => generic.Identifier.ValueText,
            MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
            MemberBindingExpressionSyntax binding => binding.Name.Identifier.ValueText,
            _ => null,
        };
    }

    private static bool IsNativeSnapshotReceiver(ExpressionSyntax expression)
    {
        if (expression is not MemberAccessExpressionSyntax member)
            return false;

        string receiver = member.Expression.ToString();
        return receiver.Contains("canvas", StringComparison.OrdinalIgnoreCase)
               || receiver.Contains("surface", StringComparison.OrdinalIgnoreCase)
               || receiver.Contains("target", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTargetFactoryInvocation(ExpressionSyntax expression)
    {
        if (expression is not MemberAccessExpressionSyntax member)
            return false;

        string receiver = member.Expression.ToString();
        return receiver.Contains("targetFactory", StringComparison.OrdinalIgnoreCase)
               || receiver.Contains("renderTargetFactory", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCpuNodeDescriptionRender(
        InvocationExpressionSyntax invocation,
        string invokedName)
    {
        if (invokedName != "Render"
            || invocation.Expression is not MemberAccessExpressionSyntax member
            || member.Expression is not InvocationExpressionSyntax getOriginal
            || GetInvokedName(getOriginal) != "GetOriginal"
            || invocation.ArgumentList.Arguments.Count < 1)
        {
            return false;
        }

        string contextName = invocation.ArgumentList.Arguments[0].Expression.ToString();
        return invocation.FirstAncestorOrSelf<MethodDeclarationSyntax>()?
            .DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .Any(variable => variable.Identifier.ValueText == contextName
                && variable.Initializer?.Value is ObjectCreationExpressionSyntax creation
                && creation.Type.DescendantTokens()
                    .LastOrDefault(static token => token.IsKind(SyntaxKind.IdentifierToken))
                    .ValueText == "GraphicsContext2D") == true;
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Beutl.slnx")))
            directory = directory.Parent;

        return directory?.FullName
               ?? throw new DirectoryNotFoundException(
                   $"Could not locate the Beutl repository root above {AppContext.BaseDirectory}.");
    }

    private static bool HasPathSegment(string path, string segment)
        => path.Split('/').Contains(segment, StringComparer.Ordinal);

    private static string NormalizePath(string path)
        => path.Replace(Path.DirectorySeparatorChar, '/');

    private static string FormatFindings(IReadOnlyList<SourceFinding> findings)
    {
        return string.Join(Environment.NewLine, findings.Take(30).Select(static finding =>
            $"  {finding.RelativePath}:{finding.Line}: {finding.Detail}: {finding.Snippet}"));
    }

    private sealed record SourceMethod(
        string RelativePath,
        SourceText Text,
        MethodDeclarationSyntax Method)
    {
        public SourceFinding ToFinding(SyntaxNode node, string detail)
        {
            LinePosition position = Text.Lines.GetLinePosition(node.SpanStart);
            string snippet = Text.Lines[position.Line].ToString().Trim();
            return new SourceFinding(RelativePath, position.Line + 1, detail, snippet);
        }
    }

    private sealed record SourceFinding(
        string RelativePath,
        int Line,
        string Detail,
        string Snippet);

    private sealed class SideEffectTripwire
    {
        public IReadOnlyDictionary<RecordingSideEffect, int> Counts => _counts;

        private readonly Dictionary<RecordingSideEffect, int> _counts = Enum
            .GetValues<RecordingSideEffect>()
            .ToDictionary(static value => value, static _ => 0);

        public void TouchAll()
        {
            foreach (RecordingSideEffect sideEffect in Enum.GetValues<RecordingSideEffect>())
            {
                _counts[sideEffect]++;
            }
        }
    }

    private enum RecordingSideEffect
    {
        GpuContext,
        TargetFactory,
        Snapshot,
        MediaRead,
        MediaDecode,
        NestedRenderer,
        Flush,
        Synchronization,
        Readback,
    }
}
