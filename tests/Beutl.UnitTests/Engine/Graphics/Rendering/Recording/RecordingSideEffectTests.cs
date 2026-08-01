using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Recording;

[TestFixture]
public sealed class RecordingSideEffectTests
{
    private static readonly MetadataReference[] s_semanticReferences = CreateSemanticReferences();

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
    public void RecordingDeferredShapes_LeavesEveryExecutionProbeAtZero()
    {
        var tripwire = new SideEffectTripwire();
        var targetFactory = new CountingTargetFactory();
        var diagnostics = new RenderPipelineDiagnosticsState();
        using var node = new DeferredShapeProbeNode(tripwire);
        using var renderer = new RenderNodeRenderer(node, new RenderNodeRendererOptions
        {
            DefaultRequest = new RenderNodeRenderRequest
            {
                TargetDomain = DeferredShapeProbeNode.Bounds,
                Diagnostics = diagnostics,
                CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
            },
            TargetFactory = targetFactory,
        });

        RenderNodeMeasurement measurement = renderer.Measure();
        RenderPipelineDiagnosticSnapshot snapshot = diagnostics.Latest;
        RenderPipelineCounter[] executionCounters =
        [
            RenderPipelineCounter.ExecutedGpuPasses,
            RenderPipelineCounter.IntermediateAcquires,
            RenderPipelineCounter.IntermediateCreates,
            RenderPipelineCounter.Synchronizations,
            RenderPipelineCounter.ProgramCreations,
            RenderPipelineCounter.OpaqueExternalExecutions,
        ];

        Assert.Multiple(() =>
        {
            Assert.That(measurement.HasFragments, Is.True);
            Assert.That(tripwire.Counts.Values, Is.All.Zero,
                "Recording and metadata resolution must not execute any deferred callback.");
            Assert.That(targetFactory.CreateCalls, Is.Zero);
            foreach (RenderPipelineCounter counter in executionCounters)
                Assert.That(snapshot[counter], Is.Zero, $"{counter} must remain zero during recording.");
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
            SyntaxTree tree = CSharpSyntaxTree.ParseText(
                text,
                CSharpParseOptions.Default.WithDocumentationMode(DocumentationMode.Parse),
                relativePath);
            CompilationUnitSyntax root = tree.GetCompilationUnitRoot();
            SemanticModel semanticModel = CSharpCompilation.Create(
                    $"RecordingSideEffectProbe_{Path.GetFileNameWithoutExtension(path)}",
                    [tree],
                    s_semanticReferences,
                    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
                .GetSemanticModel(tree, ignoreAccessibility: true);
            foreach (MethodDeclarationSyntax method in root.DescendantNodes()
                         .OfType<MethodDeclarationSyntax>()
                         .Where(IsRenderNodeProcessOverride))
            {
                yield return new SourceMethod(relativePath, text, method, semanticModel);
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

            if (name == "Snapshot" && IsNativeSnapshotInvocation(invocation, source.SemanticModel))
            {
                yield return source.ToFinding(invocation, "eager surface/target snapshot");
            }

            if ((name is null || !invocationNames.Contains(name))
                && IsTargetFactoryInvocation(invocation, source.SemanticModel))
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

    private static bool IsNativeSnapshotInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel)
    {
        IMethodSymbol? method = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        ITypeSymbol? type = method?.ContainingType;
        if (type is null && invocation.Expression is MemberAccessExpressionSyntax member)
            type = semanticModel.GetTypeInfo(member.Expression).Type;

        for (INamedTypeSymbol? current = type as INamedTypeSymbol;
             current is not null;
             current = current.BaseType)
        {
            string name = current.ToDisplayString();
            if (name is "Beutl.Graphics.Rendering.RenderTarget"
                or "Beutl.Graphics.Rendering.Renderer"
                or "SkiaSharp.SKSurface")
            {
                return true;
            }
        }

        return false;
    }

    private static MetadataReference[] CreateSemanticReferences()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string trustedPlatformAssemblies)
        {
            foreach (string path in trustedPlatformAssemblies.Split(Path.PathSeparator))
                paths.Add(path);
        }

        paths.Add(typeof(RenderNode).Assembly.Location);
        paths.Add(typeof(SKSurface).Assembly.Location);
        return paths
            .Where(static path => !string.IsNullOrEmpty(path) && File.Exists(path))
            .Select(static path => MetadataReference.CreateFromFile(path))
            .ToArray();
    }

    [Test]
    public void TargetFactoryProbe_ResolvesReceiversByType()
    {
        const string source = """
            using Beutl.Graphics.Rendering;

            public sealed class Probe
            {
                public void Run(IRenderTargetFactory allocator)
                {
                    allocator.Create(default);
                }
            }
            """;
        SyntaxTree tree = CSharpSyntaxTree.ParseText(source);
        CSharpCompilation compilation = CSharpCompilation.Create(
            "TargetFactoryReceiverProbe",
            [tree],
            s_semanticReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        SemanticModel semanticModel = compilation.GetSemanticModel(tree);
        InvocationExpressionSyntax invocation = tree.GetRoot()
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Single();

        Assert.That(IsTargetFactoryInvocation(invocation, semanticModel), Is.True,
            "An IRenderTargetFactory receiver named 'allocator' must remain inside the recording gate.");
    }

    private static bool IsTargetFactoryInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel)
    {
        IMethodSymbol? method = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        ITypeSymbol? receiverType = method?.ContainingType;
        if (receiverType is null && invocation.Expression is MemberAccessExpressionSyntax member)
            receiverType = semanticModel.GetTypeInfo(member.Expression).Type;

        return receiverType is INamedTypeSymbol named
               && IsRenderTargetFactoryType(named);
    }

    private static bool IsRenderTargetFactoryType(INamedTypeSymbol type)
        => IsRenderTargetFactoryInterface(type)
           || type.AllInterfaces.Any(IsRenderTargetFactoryInterface);

    private static bool IsRenderTargetFactoryInterface(INamedTypeSymbol type)
        => type.ToDisplayString() == "Beutl.Graphics.Rendering.IRenderTargetFactory";

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
        MethodDeclarationSyntax Method,
        SemanticModel SemanticModel)
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

    private sealed class DeferredShapeProbeNode(SideEffectTripwire tripwire) : RenderNode
    {
        public static Rect Bounds { get; } = new(0, 0, 64, 36);

        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle source = context.OpaqueSource(CreateOpaque(
                OpaqueRenderBoundsContract.Source(Bounds),
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale,
                "source",
                inputCount: 0));
            RenderFragmentHandle mapped = context.OpaqueMap(
                source,
                CreateOpaque(
                    OpaqueRenderBoundsContract.Map(RenderBoundsContract.Identity),
                    RenderValueCardinality.Single,
                    RenderScaleContract.PreserveInputSupply,
                    "map",
                    inputCount: 1));
            RenderFragmentHandle combined = context.OpaqueCombine(
                [source, mapped],
                CreateOpaque(
                    OpaqueRenderBoundsContract.Combine(
                        static inputs => inputs.Aggregate(static (left, right) => left.Union(right)),
                        static (output, inputs) => inputs.Select(_ => output).ToArray(),
                        "recording-probe-combine-bounds"),
                    RenderValueCardinality.Single,
                    RenderScaleContract.MaterializeAtWorkingScale,
                    "combine",
                    inputCount: 2));
            RenderFragmentHandle expanded = context.OpaqueExpand(
                [source],
                CreateOpaque(
                    OpaqueRenderBoundsContract.FullInputs(
                        static inputs => inputs.Aggregate(static (left, right) => left.Union(right)),
                        "recording-probe-expand-bounds"),
                    RenderValueCardinality.Dynamic,
                    RenderScaleContract.MaterializeAtWorkingScale,
                    "expand",
                    inputCount: 1));
            RenderFragmentHandle shader = context.Shader(
                source,
                ShaderDescription.CurrentPixel(
                    "half4 apply(half4 color) { return color; }"));
            RenderFragmentHandle geometry = context.Geometry(
                source,
                GeometryDescription.Create(
                    _ => tripwire.TouchAll(),
                    RenderBoundsContract.Identity,
                    RenderHitTestContract.AnyInput,
                    structuralKey: "geometry-recording-probe",
                    requiresReadback: true));
            RenderFragmentHandle capture = context.TargetCapture(
                TargetCaptureDescription.Create(
                    TargetRegion.Full,
                    Bounds,
                    RenderHitTestContract.OutputBounds,
                    RenderScaleContract.MaterializeAtWorkingScale));
            RenderFragmentHandle command = context.TargetCommand(
                [source],
                TargetCommandDescription.Create(
                    _ => tripwire.TouchAll(),
                    TargetRegion.Full,
                    Bounds,
                    RenderHitTestContract.OutputBounds,
                    TargetAccess.Readback,
                    inputReadbacks: [RenderInputReadback.All],
                    structuralKey: "target-command-recording-probe"));
            RenderFragmentHandle scope = context.TargetScope(
                source,
                TargetScopeDescription.Create(
                    _ => tripwire.TouchAll(),
                    RenderBoundsContract.Identity,
                    RenderHitTestContract.AnyInput,
                    RenderScaleContract.PreserveInputSupply,
                    structuralKey: "target-scope-recording-probe"));
            RenderFragmentHandle rawScope = context.RawTargetScope(
                source,
                RawTargetScopeDescription.Create(
                    _ => tripwire.TouchAll(),
                    RenderBoundsContract.Identity,
                    RenderHitTestContract.AnyInput,
                    RenderScaleContract.PreserveInputSupply,
                    structuralKey: "raw-target-scope-recording-probe"));
            RenderFragmentHandle rawCommand = context.RawTargetCommand(
                RawTargetCommandDescription.Create(
                    _ => tripwire.TouchAll(),
                    Bounds,
                    RenderHitTestContract.OutputBounds,
                    structuralKey: "raw-target-command-recording-probe"));

            context.PublishRange(
                [source, mapped, combined, expanded, shader, geometry, capture, command, scope, rawScope, rawCommand]);
        }

        private OpaqueRenderDescription CreateOpaque(
            OpaqueRenderBoundsContract bounds,
            RenderValueCardinality cardinality,
            RenderScaleContract scale,
            string key,
            int inputCount)
        {
            return OpaqueRenderDescription.Create(
                _ => tripwire.TouchAll(),
                bounds,
                RenderHitTestContract.OutputBounds,
                cardinality,
                scale,
                structuralKey: $"opaque-recording-probe-{key}",
                inputReadbacks: Enumerable.Repeat(RenderInputReadback.All, inputCount));
        }
    }

    private sealed class CountingTargetFactory : IRenderTargetFactory
    {
        public int CreateCalls { get; private set; }

        public RenderTarget Create(RenderTargetAllocationDescriptor allocation)
        {
            PixelSize deviceSize = allocation.DeviceSize;
            CreateCalls++;
            throw new AssertionException(
                $"Recording unexpectedly requested a {deviceSize.Width}x{deviceSize.Height} render target.");
        }
    }

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
