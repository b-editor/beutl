using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Recording;

[TestFixture]
public sealed class RenderPipelineMigrationCensusTests
{
    private const string HistoricalEvidencePatch =
        "docs/specs/004-gpu-pass-fusion/evidence/target-baseline-generator.patch";

    private static readonly Lazy<SourceCorpus> s_corpus = new(SourceCorpus.Discover);

    private static readonly IReadOnlyDictionary<string, int> s_productionOverrideBaseline =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["src/Beutl.Engine/Graphics/AudioVisualizers/AudioVisualizerRenderNode.cs"] = 1,
            ["src/Beutl.Engine/Graphics/DrawableGroup.cs"] = 2,
            ["src/Beutl.Engine/Graphics/Particles/ParticleRenderNode.cs"] = 1,
            ["src/Beutl.Engine/Graphics/Rendering/BlendModeRenderNode.cs"] = 1,
            ["src/Beutl.Engine/Graphics/Rendering/ClearRenderNode.cs"] = 1,
            ["src/Beutl.Engine/Graphics/Rendering/ContainerRenderNode.cs"] = 1,
            ["src/Beutl.Engine/Graphics/Rendering/DrawBackdropRenderNode.cs"] = 1,
            ["src/Beutl.Engine/Graphics/Rendering/EllipseRenderNode.cs"] = 1,
            ["src/Beutl.Engine/Graphics/Rendering/FilterEffectRenderNode.cs"] = 1,
            ["src/Beutl.Engine/Graphics/Rendering/GeometryClipRenderNode.cs"] = 1,
            ["src/Beutl.Engine/Graphics/Rendering/GeometryRenderNode.cs"] = 1,
            ["src/Beutl.Engine/Graphics/Rendering/ImageSourceRenderNode.cs"] = 1,
            ["src/Beutl.Engine/Graphics/Rendering/LayerRenderNode.cs"] = 1,
            ["src/Beutl.Engine/Graphics/Rendering/MemoryNode.cs"] = 1,
            ["src/Beutl.Engine/Graphics/Rendering/OpacityMaskRenderNode.cs"] = 1,
            ["src/Beutl.Engine/Graphics/Rendering/OpacityRenderNode.cs"] = 1,
            ["src/Beutl.Engine/Graphics/Rendering/PushRenderNode.cs"] = 1,
            ["src/Beutl.Engine/Graphics/Rendering/RectClipRenderNode.cs"] = 1,
            ["src/Beutl.Engine/Graphics/Rendering/RectangleRenderNode.cs"] = 1,
            ["src/Beutl.Engine/Graphics/Rendering/ReferencesChildRenderNode.cs"] = 1,
            ["src/Beutl.Engine/Graphics/Rendering/SnapshotBackdropRenderNode.cs"] = 1,
            ["src/Beutl.Engine/Graphics/Rendering/TextRenderNode.cs"] = 1,
            ["src/Beutl.Engine/Graphics/Rendering/TransformRenderNode.cs"] = 1,
            ["src/Beutl.Engine/Graphics/Rendering/VideoSourceRenderNode.cs"] = 1,
            ["src/Beutl.Engine/Graphics3D/Scene3DRenderNode.cs"] = 1,
            ["src/Beutl.NodeGraph/NodeGraphFilterEffectRenderNode.cs"] = 1,
            ["src/Beutl.ProjectSystem/ProjectSystem/SceneDrawable.cs"] = 1,
        };

    private static readonly IReadOnlyDictionary<string, int> s_startingProductionOverrideBaseline =
        s_productionOverrideBaseline
            .Append(new KeyValuePair<string, int>(
                "src/Beutl.Engine/Graphics/Rendering/OperationWrapperRenderNode.cs",
                1))
            .ToDictionary(static item => item.Key, static item => item.Value, StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, int> s_testOverrideBaseline =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/NodeCacheScaleTests.cs"] = 1,
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/RendererExceptionSafetyTests.cs"] = 1,
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/SourceEffectiveScaleFlowTests.cs"] = 10,
            ["tests/Beutl.UnitTests/NodeGraph/NodeGraphFilterEffectRenderNodeTests.cs"] = 5,
        };

    private static readonly IReadOnlyDictionary<string, int> s_startingTestOverrideBaseline =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/NodeCacheScaleTests.cs"] = 1,
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/RenderNodeProcessorExceptionSafetyTests.cs"] = 1,
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/RendererExceptionSafetyTests.cs"] = 1,
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/SourceEffectiveScaleFlowTests.cs"] = 3,
            ["tests/Beutl.UnitTests/NodeGraph/NodeGraphFilterEffectRenderNodeTests.cs"] = 1,
        };

    [Test]
    public void SourceScope_IsOnlyCheckedInCSharpAndExcludesHistoricalEvidence()
    {
        SourceCorpus corpus = s_corpus.Value;
        string[] outsideScope = corpus.Documents
            .Where(document =>
                !document.RelativePath.StartsWith("src/", StringComparison.Ordinal)
                && !document.RelativePath.StartsWith("tests/", StringComparison.Ordinal))
            .Select(document => document.RelativePath)
            .ToArray();
        string[] buildOutputs = corpus.Documents
            .Where(document => HasPathSegment(document.RelativePath, "bin")
                || HasPathSegment(document.RelativePath, "obj"))
            .Select(document => document.RelativePath)
            .ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(corpus.Documents, Is.Not.Empty);
            Assert.That(outsideScope, Is.Empty);
            Assert.That(buildOutputs, Is.Empty);
            Assert.That(corpus.Documents.Select(document => document.RelativePath),
                Does.Not.Contain(HistoricalEvidencePatch));
            Assert.That(File.Exists(Path.Combine(corpus.RepositoryRoot, HistoricalEvidencePatch)), Is.True,
                "The historical patch should exist but remain outside the compiled-source corpus.");
        }
    }

    [Test]
    public void ProcessOverrideInventory_PinsStartingBaselineAndMigratedOverrides()
    {
        IReadOnlyList<SourceMethod> overrides = s_corpus.Value.FindRenderNodeProcessOverrides();

        using (Assert.EnterMultipleScope())
        {
            AssertDeclaredBaseline("production", 29, s_startingProductionOverrideBaseline);
            AssertDeclaredBaseline("test", 7, s_startingTestOverrideBaseline);
            AssertBaselineInventory("production", 28, s_productionOverrideBaseline, overrides);
            AssertBaselineInventory("test", 17, s_testOverrideBaseline, overrides);
        }
    }

    [Test]
    public void ProcessOverrides_UseTheVoidRecordingContract()
    {
        IEnumerable<SourceFinding> findings = s_corpus.Value.FindRenderNodeProcessOverrides()
            .Where(sourceMethod => !ReturnsVoid(sourceMethod.Method))
            .Select(sourceMethod => sourceMethod.ToFinding(
                $"returns '{sourceMethod.Method.ReturnType}'"));

        AssertNoFindings("Every render-node Process override must return void.", findings);
    }

    [Test]
    public void ExecutableOperationTypeAndFactories_AreRemoved()
    {
        string operationType = BuildName("Render", "Node", "Operation");
        string[] factoryNames =
        [
            BuildName("Create", "Lambda"),
            BuildName("Create", "Decorator"),
            BuildName("Create", "From", "Render", "Target"),
            BuildName("Create", "From", "Surface"),
        ];

        using (Assert.EnterMultipleScope())
        {
            AssertNoFindings($"The executable type '{operationType}' must be absent.",
                s_corpus.Value.FindWord(operationType));
            AssertNoFindings("Executable operation factories must be absent.",
                factoryNames.SelectMany(s_corpus.Value.FindWord));
        }
    }

    [Test]
    public void ProcessorPullApis_AreRemoved()
    {
        string[] pullNames =
        [
            BuildName("Pu", "ll"),
            BuildName("Pu", "ll", "To", "Root"),
        ];

        AssertNoFindings("Processor pull APIs must be absent.",
            pullNames.SelectMany(s_corpus.Value.FindWord));
    }

    [Test]
    public void ListRasterizationCompatibility_IsRemoved()
    {
        string[] compatibilityNames =
        [
            BuildName("Rasterize", "To", "Render", "Targets"),
            BuildName("Rasterize", "And", "Concat"),
        ];
        IEnumerable<SourceFinding> findings = s_corpus.Value.FindLegacyRasterizers()
            .Concat(compatibilityNames.SelectMany(s_corpus.Value.FindWord));

        AssertNoFindings("Rasterization must return one owned RenderNodeRasterization, not a list or compatibility result.",
            findings);
    }

    [Test]
    public void OperationRetentionAndBackedEffectTargets_AreRemoved()
    {
        string setterName = BuildName("Set", "Operations");
        string operationPropertyName = BuildName("Node", "Operation");
        string operationType = BuildName("Render", "Node", "Operation");

        using (Assert.EnterMultipleScope())
        {
            AssertNoFindings("Operation wrappers must not retain executable results.",
                s_corpus.Value.FindWord(setterName));
            AssertNoFindings("Effect targets must not expose an operation-backed property.",
                s_corpus.Value.FindWord(operationPropertyName));
            AssertNoFindings("Effect targets must have only materialized-target construction paths.",
                s_corpus.Value.FindOperationBackedEffectTargets(operationType));
        }
    }

    [Test]
    public void ProcessMethods_DoNotCreateIsolatedNestedRenderers()
    {
        string[] rendererTypes =
        [
            BuildName("Render", "Node", "Processor"),
            BuildName("Render", "Node", "Renderer"),
        ];

        AssertNoFindings("Nested nodes must record through the current context instead of creating an isolated renderer.",
            s_corpus.Value.FindNamedTokensInsideRenderNodeProcess(rendererTypes));
    }

    [Test]
    public void CacheGeneration_DoesNotStartAnIndependentPullOrRasterization()
    {
        string[] forbiddenNames =
        [
            BuildName("Render", "Node", "Processor"),
            BuildName("Render", "Node", "Renderer"),
            BuildName("Pu", "ll"),
            BuildName("Pu", "ll", "To", "Root"),
            BuildName("Rasterize"),
            BuildName("Rasterize", "To", "Render", "Targets"),
            BuildName("Rasterize", "And", "Concat"),
        ];

        AssertNoFindings("Cache generation must be resolved inside the current request.",
            s_corpus.Value.FindForbiddenCacheExecution(forbiddenNames));
    }

    [Test]
    public void RawCanvasCallbacks_AreExplicitlyClassified()
    {
        string[] callbackFactoryNames =
        [
            BuildName("Create", "Lambda"),
            BuildName("Create", "Decorator"),
        ];

        AssertNoFindings(
            "Raw callbacks must use a typed, guarded opaque, or explicitly raw description.",
            s_corpus.Value.FindInvocations(callbackFactoryNames));
    }

    [Test]
    public void ContextScaleHelpers_AreMovedWithoutForwardingMembers()
    {
        string contextType = BuildName("Render", "Node", "Context");
        string[] helperNames =
        [
            BuildName("Max", "Buffer", "Dimension"),
            BuildName("Sanitize", "Max", "Working", "Scale"),
            BuildName("Resolve", "Working", "Scale"),
            BuildName("Clamp", "Working", "Scale", "To", "Buffer", "Budget"),
        ];
        IEnumerable<SourceFinding> findings = s_corpus.Value.FindQualifiedReferences(contextType, helperNames)
            .Concat(s_corpus.Value.FindMembersDeclaredByType(contextType, helperNames));

        AssertNoFindings("Scale helpers must be owned only by RenderScaleUtilities.", findings);
    }

    private static void AssertBaselineInventory(
        string label,
        int expectedCount,
        IReadOnlyDictionary<string, int> expected,
        IReadOnlyList<SourceMethod> allOverrides)
    {
        SourceMethod[] baselineOverrides = allOverrides
            .Where(sourceMethod => expected.ContainsKey(sourceMethod.Document.RelativePath))
            .ToArray();
        string[] expectedInventory = FormatInventory(expected);
        string[] actualInventory = FormatInventory(baselineOverrides
            .GroupBy(sourceMethod => sourceMethod.Document.RelativePath, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal));

        Assert.That(expected.Values.Sum(), Is.EqualTo(expectedCount),
            $"The checked-in {label} baseline declaration is inconsistent.");
        Assert.That(baselineOverrides, Has.Length.EqualTo(expectedCount),
            $"The {label} Process override baseline changed.{Environment.NewLine}{FormatMethods(baselineOverrides)}");
        Assert.That(actualInventory, Is.EqualTo(expectedInventory),
            $"The {label} Process override inventory changed.");
    }

    private static void AssertDeclaredBaseline(
        string label,
        int expectedCount,
        IReadOnlyDictionary<string, int> expected)
    {
        Assert.That(expected.Values.Sum(), Is.EqualTo(expectedCount),
            $"The checked-in starting-SHA {label} baseline declaration is inconsistent.");
    }

    private static string[] FormatInventory(IReadOnlyDictionary<string, int> inventory)
    {
        return inventory
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{pair.Key}#{pair.Value}")
            .ToArray();
    }

    private static string FormatMethods(IEnumerable<SourceMethod> methods)
    {
        return string.Join(Environment.NewLine, methods
            .OrderBy(sourceMethod => sourceMethod.Document.RelativePath, StringComparer.Ordinal)
            .ThenBy(sourceMethod => sourceMethod.Line)
            .Select(sourceMethod => $"  {sourceMethod.Document.RelativePath}:{sourceMethod.Line}"));
    }

    private static void AssertNoFindings(string requirement, IEnumerable<SourceFinding> findings)
    {
        SourceFinding[] materialized = findings
            .Distinct()
            .OrderBy(finding => finding.RelativePath, StringComparer.Ordinal)
            .ThenBy(finding => finding.Line)
            .ThenBy(finding => finding.Detail, StringComparer.Ordinal)
            .ToArray();

        Assert.That(materialized, Is.Empty, $"{requirement}{Environment.NewLine}{FormatFindings(materialized)}");
    }

    private static string FormatFindings(IReadOnlyList<SourceFinding> findings)
    {
        const int maximumReportedFindings = 30;
        IEnumerable<string> lines = findings.Take(maximumReportedFindings)
            .Select(finding =>
                $"  {finding.RelativePath}:{finding.Line}: {finding.Detail}: {finding.Snippet}");
        string result = string.Join(Environment.NewLine, lines);
        if (findings.Count > maximumReportedFindings)
        {
            result += Environment.NewLine
                + $"  ... {findings.Count - maximumReportedFindings} more finding(s)";
        }

        return result;
    }

    private static bool ReturnsVoid(MethodDeclarationSyntax method)
    {
        return method.ReturnType is PredefinedTypeSyntax predefined
            && predefined.Keyword.IsKind(SyntaxKind.VoidKeyword);
    }

    private static bool IsNamedType(TypeSyntax? type, string expectedName)
    {
        return type?.DescendantTokens()
            .LastOrDefault(token => token.IsKind(SyntaxKind.IdentifierToken))
            .ValueText == expectedName;
    }

    private static bool IsRenderNodeProcess(MethodDeclarationSyntax method)
    {
        return method.Identifier.ValueText == "Process"
            && method.ParameterList.Parameters.Count == 1
            && IsNamedType(method.ParameterList.Parameters[0].Type, "RenderNodeContext");
    }

    private static string BuildName(params string[] parts)
    {
        return string.Concat(parts);
    }

    private static bool HasPathSegment(string path, string segment)
    {
        return path.Split('/').Contains(segment, StringComparer.Ordinal);
    }

    private sealed class SourceCorpus
    {
        private SourceCorpus(string repositoryRoot, IReadOnlyList<SourceDocument> documents)
        {
            RepositoryRoot = repositoryRoot;
            Documents = documents;
        }

        public string RepositoryRoot { get; }

        public IReadOnlyList<SourceDocument> Documents { get; }

        public static SourceCorpus Discover()
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Beutl.slnx")))
                directory = directory.Parent;

            if (directory is null)
            {
                throw new DirectoryNotFoundException(
                    $"Could not locate the Beutl repository root above {AppContext.BaseDirectory}.");
            }

            string repositoryRoot = directory.FullName;
            var documents = new List<SourceDocument>();
            foreach (string sourceRootName in new[] { "src", "tests" })
            {
                string sourceRoot = Path.Combine(repositoryRoot, sourceRootName);
                foreach (string path in Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories))
                {
                    string relativePath = NormalizePath(Path.GetRelativePath(repositoryRoot, path));
                    if (HasPathSegment(relativePath, "bin") || HasPathSegment(relativePath, "obj"))
                        continue;

                    SourceText text = SourceText.From(File.ReadAllText(path));
                    var tree = CSharpSyntaxTree.ParseText(
                        text,
                        CSharpParseOptions.Default.WithDocumentationMode(DocumentationMode.Parse),
                        relativePath);
                    documents.Add(new SourceDocument(
                        relativePath,
                        text,
                        tree.GetCompilationUnitRoot()));
                }
            }

            documents.Sort((left, right) =>
                StringComparer.Ordinal.Compare(left.RelativePath, right.RelativePath));
            return new SourceCorpus(repositoryRoot, documents);
        }

        public IReadOnlyList<SourceMethod> FindRenderNodeProcessOverrides()
        {
            return Documents.SelectMany(document => document.Root.DescendantNodes()
                    .OfType<MethodDeclarationSyntax>()
                    .Where(method => method.Modifiers.Any(SyntaxKind.OverrideKeyword) && IsRenderNodeProcess(method))
                    .Select(method => new SourceMethod(document, method)))
                .ToArray();
        }

        public IEnumerable<SourceFinding> FindWord(string value)
        {
            foreach (SourceDocument document in Documents)
            {
                foreach (SyntaxToken token in document.Root.DescendantTokens()
                             .Where(token => token.IsKind(SyntaxKind.IdentifierToken)
                                 && token.ValueText == value))
                {
                    yield return document.ToFinding(token, $"reference to '{value}'");
                }
            }
        }

        public IEnumerable<SourceFinding> FindQualifiedReferences(
            string containingType,
            IReadOnlyList<string> memberNames)
        {
            string alternatives = string.Join("|", memberNames.Select(Regex.Escape));
            var pattern = new Regex(
                $@"(?<![\p{{L}}\p{{N}}_]){Regex.Escape(containingType)}\s*\.\s*(?:{alternatives})(?![\p{{L}}\p{{N}}_])",
                RegexOptions.CultureInvariant);
            return FindText(pattern, $"reference through '{containingType}'");
        }

        public IEnumerable<SourceFinding> FindInvocations(IReadOnlyCollection<string> names)
        {
            var nameSet = new HashSet<string>(names, StringComparer.Ordinal);
            foreach (SourceDocument document in Documents)
            {
                foreach (InvocationExpressionSyntax invocation in document.Root.DescendantNodes()
                             .OfType<InvocationExpressionSyntax>())
                {
                    string? name = GetInvokedName(invocation);
                    if (name is not null && nameSet.Contains(name))
                        yield return document.ToFinding(invocation, $"invocation of '{name}'");
                }
            }
        }

        public IEnumerable<SourceFinding> FindLegacyRasterizers()
        {
            foreach (SourceDocument document in Documents)
            {
                foreach (MethodDeclarationSyntax method in document.Root.DescendantNodes()
                             .OfType<MethodDeclarationSyntax>()
                             .Where(method => method.Identifier.ValueText == "Rasterize"
                                 && !IsNamedType(method.ReturnType, "RenderNodeRasterization")))
                {
                    yield return document.ToFinding(method,
                        $"Rasterize returns '{method.ReturnType}'");
                }
            }
        }

        public IEnumerable<SourceFinding> FindOperationBackedEffectTargets(string operationType)
        {
            foreach (SourceDocument document in Documents)
            {
                foreach (ConstructorDeclarationSyntax constructor in document.Root.DescendantNodes()
                             .OfType<ConstructorDeclarationSyntax>()
                             .Where(constructor => constructor.Identifier.ValueText == "EffectTarget"
                                 && constructor.ParameterList.Parameters.Count == 1
                                 && IsNamedType(constructor.ParameterList.Parameters[0].Type, operationType)))
                {
                    yield return document.ToFinding(constructor,
                        "operation-backed EffectTarget constructor");
                }

                foreach (ObjectCreationExpressionSyntax creation in document.Root.DescendantNodes()
                             .OfType<ObjectCreationExpressionSyntax>()
                             .Where(creation => IsNamedType(creation.Type, "EffectTarget")
                                 && creation.ArgumentList?.Arguments.Count == 1))
                {
                    yield return document.ToFinding(creation,
                        "one-argument EffectTarget construction");
                }
            }
        }

        public IEnumerable<SourceFinding> FindNamedTokensInsideRenderNodeProcess(
            IReadOnlyCollection<string> names)
        {
            var nameSet = new HashSet<string>(names, StringComparer.Ordinal);
            foreach (SourceDocument document in Documents)
            {
                foreach (MethodDeclarationSyntax method in document.Root.DescendantNodes()
                             .OfType<MethodDeclarationSyntax>()
                             .Where(IsRenderNodeProcess))
                {
                    foreach (SyntaxToken token in method.DescendantTokens()
                                 .Where(token => token.IsKind(SyntaxKind.IdentifierToken)
                                     && nameSet.Contains(token.ValueText)))
                    {
                        yield return document.ToFinding(token,
                            $"isolated renderer '{token.ValueText}' inside Process");
                    }
                }
            }
        }

        public IEnumerable<SourceFinding> FindForbiddenCacheExecution(IReadOnlyCollection<string> names)
        {
            var nameSet = new HashSet<string>(names, StringComparer.Ordinal);
            string cacheHelperType = BuildName("Render", "Node", "Cache", "Helper");
            string[] cacheMethodNames =
            [
                BuildName("Make", "Cache"),
                BuildName("Create", "Default", "Cache"),
            ];

            foreach (SourceDocument document in Documents)
            {
                IEnumerable<SyntaxNode> roots = document.Root.DescendantNodes()
                    .OfType<TypeDeclarationSyntax>()
                    .Where(type => type.Identifier.ValueText == cacheHelperType)
                    .Cast<SyntaxNode>()
                    .Concat(document.Root.DescendantNodes()
                        .OfType<MethodDeclarationSyntax>()
                        .Where(method => cacheMethodNames.Contains(method.Identifier.ValueText, StringComparer.Ordinal)));

                foreach (SyntaxNode root in roots)
                {
                    foreach (SyntaxToken token in root.DescendantTokens()
                                 .Where(token => token.IsKind(SyntaxKind.IdentifierToken)
                                     && nameSet.Contains(token.ValueText)))
                    {
                        yield return document.ToFinding(token,
                            $"independent cache execution '{token.ValueText}'");
                    }
                }
            }
        }

        public IEnumerable<SourceFinding> FindMembersDeclaredByType(
            string typeName,
            IReadOnlyCollection<string> memberNames)
        {
            var memberNameSet = new HashSet<string>(memberNames, StringComparer.Ordinal);
            foreach (SourceDocument document in Documents)
            {
                foreach (TypeDeclarationSyntax type in document.Root.DescendantNodes()
                             .OfType<TypeDeclarationSyntax>()
                             .Where(type => type.Identifier.ValueText == typeName))
                {
                    foreach (MemberDeclarationSyntax member in type.Members)
                    {
                        foreach (SyntaxToken identifier in GetDeclaredIdentifiers(member)
                                     .Where(token => memberNameSet.Contains(token.ValueText)))
                        {
                            yield return document.ToFinding(identifier,
                                $"forwarding member '{identifier.ValueText}' on '{typeName}'");
                        }
                    }
                }
            }
        }

        private IEnumerable<SourceFinding> FindText(Regex pattern, string detail)
        {
            foreach (SourceDocument document in Documents)
            {
                foreach (Match match in pattern.Matches(document.Text.ToString()))
                    yield return document.ToFinding(match.Index, detail);
            }
        }

        private static string? GetInvokedName(InvocationExpressionSyntax invocation)
        {
            return invocation.Expression switch
            {
                IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
                GenericNameSyntax generic => generic.Identifier.ValueText,
                MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
                MemberBindingExpressionSyntax memberBinding => memberBinding.Name.Identifier.ValueText,
                _ => null,
            };
        }

        private static IEnumerable<SyntaxToken> GetDeclaredIdentifiers(MemberDeclarationSyntax member)
        {
            return member switch
            {
                MethodDeclarationSyntax method => [method.Identifier],
                PropertyDeclarationSyntax property => [property.Identifier],
                EventDeclarationSyntax eventDeclaration => [eventDeclaration.Identifier],
                FieldDeclarationSyntax field => field.Declaration.Variables.Select(variable => variable.Identifier),
                EventFieldDeclarationSyntax eventField =>
                    eventField.Declaration.Variables.Select(variable => variable.Identifier),
                _ => [],
            };
        }

        private static string NormalizePath(string path)
        {
            return path.Replace(Path.DirectorySeparatorChar, '/');
        }
    }

    private sealed record SourceDocument(
        string RelativePath,
        SourceText Text,
        CompilationUnitSyntax Root)
    {
        public SourceFinding ToFinding(SyntaxNode node, string detail)
        {
            return ToFinding(node.SpanStart, detail);
        }

        public SourceFinding ToFinding(SyntaxToken token, string detail)
        {
            return ToFinding(token.SpanStart, detail);
        }

        public SourceFinding ToFinding(int position, string detail)
        {
            LinePosition linePosition = Text.Lines.GetLinePosition(position);
            string snippet = Text.Lines[linePosition.Line].ToString().Trim();
            if (snippet.Length > 180)
                snippet = snippet[..177] + "...";

            return new SourceFinding(RelativePath, linePosition.Line + 1, detail, snippet);
        }
    }

    private sealed record SourceMethod(SourceDocument Document, MethodDeclarationSyntax Method)
    {
        public int Line => Method.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

        public SourceFinding ToFinding(string detail)
        {
            return Document.ToFinding(Method, detail);
        }
    }

    private sealed record SourceFinding(
        string RelativePath,
        int Line,
        string Detail,
        string Snippet);
}
