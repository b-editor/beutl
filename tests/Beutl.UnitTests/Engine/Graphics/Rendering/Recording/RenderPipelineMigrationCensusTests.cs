using System.Text.RegularExpressions;

using Beutl.UnitTests.Engine.Graphics.Rendering.Baseline;

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
            ["src/Beutl.Engine/Graphics/DrawableGroup.cs"] = 3,
            ["src/Beutl.Engine/Graphics/Particles/ParticleRenderNode.cs"] = 1,
            ["src/Beutl.Engine/Graphics/Rendering/BlendModeRenderNode.cs"] = 1,
            ["src/Beutl.Engine/Graphics/Rendering/ClearRenderNode.cs"] = 1,
            ["src/Beutl.Engine/Graphics/Rendering/CompleteTargetRenderNode.cs"] = 1,
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
            ["src/Beutl.NodeGraph/Nodes/FilterEffectInputRenderNode.cs"] = 1,
            ["src/Beutl.ProjectSystem/ProjectSystem/SceneDrawable.cs"] = 1,
        };

    // The starting-SHA baseline is a historical fact about 83e63689d; overrides that first
    // appeared during the migration are excluded from the derivation.
    private static readonly IReadOnlyDictionary<string, int> s_startingProductionOverrideBaseline =
        s_productionOverrideBaseline
            .Where(static item =>
                item.Key != "src/Beutl.Engine/Graphics/Rendering/CompleteTargetRenderNode.cs"
                && item.Key != "src/Beutl.Engine/Graphics/DrawableGroup.cs"
                && item.Key != "src/Beutl.NodeGraph/Nodes/FilterEffectInputRenderNode.cs")
            .Append(new KeyValuePair<string, int>(
                "src/Beutl.Engine/Graphics/DrawableGroup.cs",
                2))
            .Append(new KeyValuePair<string, int>(
                "src/Beutl.Engine/Graphics/Rendering/OperationWrapperRenderNode.cs",
                1))
            .ToDictionary(static item => item.Key, static item => item.Value, StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, int> s_testOverrideBaseline =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["tests/Beutl.Benchmarks/Rendering/RenderPipelineBenchmarks.cs"] = 6,
            ["tests/Beutl.Graphics3DTests/GpuPassFusion3DBoundaryTests.cs"] = 1,
            ["tests/Beutl.Graphics3DTests/ShaderDescriptionSpirvEquivalenceTests.cs"] = 1,
            ["tests/Beutl.PublicApiContractTests/CapturedResourceBorrowContractTests.cs"] = 1,
            ["tests/Beutl.PublicApiContractTests/DeclaredPlannerTraitContractTests.cs"] = 2,
            ["tests/Beutl.PublicApiContractTests/DeclaredResourceAddressingContractTests.cs"] = 1,
            ["tests/Beutl.PublicApiContractTests/DetachedResourceAuthoringContractTests.cs"] = 1,
            ["tests/Beutl.PublicApiContractTests/FilterEffectCompatibilityContractTests.cs"] = 5,
            ["tests/Beutl.PublicApiContractTests/RenderNodeContextMetadataContractTests.cs"] = 3,
            ["tests/Beutl.PublicApiContractTests/GeometryAuthoringContractTests.cs"] = 2,
            ["tests/Beutl.PublicApiContractTests/OpaqueOutputPublicationContractTests.cs"] = 1,
            ["tests/Beutl.PublicApiContractTests/OpaqueSourceStateContractTests.cs"] = 2,
            ["tests/Beutl.PublicApiContractTests/OrphanedTargetEffectContractTests.cs"] = 2,
            ["tests/Beutl.PublicApiContractTests/PaintedSourceAuthoringContractTests.cs"] = 3,
            ["tests/Beutl.PublicApiContractTests/RenderDescriptionPublicSurfaceContractTests.cs"] = 1,
            ["tests/Beutl.PublicApiContractTests/RenderNodeAuthoringContractTests.cs"] = 2,
            ["tests/Beutl.PublicApiContractTests/RenderNodeRendererContractTests.cs"] = 1,
            ["tests/Beutl.PublicApiContractTests/RenderScaleMappingContractTests.cs"] = 7,
            ["tests/Beutl.PublicApiContractTests/ShaderAuthoringContractTests.cs"] = 1,
            ["tests/Beutl.PublicApiContractTests/TargetAuthoringContractTests.cs"] = 2,
            ["tests/Beutl.UnitTests/Engine/Graphics/FilterEffects/ShaderDescriptionOffsetMetadataTests.cs"] = 1,
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/BrushIntermediateAllocationIntentTests.cs"] = 3,
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/Cache/DegradedPreviewCachePurityTests.cs"] = 2,
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/Cache/OutputIdentityFanOutCostTests.cs"] = 2,
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/Cache/ContributeValuesCacheHitExecutionTests.cs"] = 4,
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/Cache/RenderCacheIdentityChannelTests.cs"] = 5,
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/Cache/RenderCacheResolutionTests.cs"] = 4,
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/Cache/ShaderRequestScaleIdentityTests.cs"] = 1,
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/Cache/MetadataCallbackIdentityTests.cs"] = 1,
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/Cache/RenderNodeCacheHelperTest.cs"] = 2,
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/Cache/StructuralAndProgramCacheTests.cs"] = 6,
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/ContainerRenderNodeTest.cs"] = 1,
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/DeviceBufferBudgetTests.cs"] = 1,
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/DirectSkiaFilterReplayTests.cs"] = 2,
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/EmptyOpaquePublishTests.cs"] = 1,
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/EngineResourceIdentityRoutingTests.cs"] = 1,
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/Failure/DeferredCallbackFailureTests.cs"] = 7,
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/Failure/NestedTargetAndCleanupFailureTests.cs"] = 16,
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/Failure/RecordingAndPlanningFailureTests.cs"] = 6,
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/Failure/RenderNodeRendererLifetimeTests.cs"] = 1,
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/Failure/ShaderAndAllocationFailureTests.cs"] = 4,
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/Fusion/CrossNodeShaderFusionTests.cs"] = 5,
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/Fusion/ExecutionIslandAuthorityTests.cs"] = 3,
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/Fusion/FusionBoundaryExecutionTestSupport.cs"] = 6,
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/Fusion/ShaderFallbackTests.cs"] = 2,
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/Golden/DirectBlurFiniteOutputTests.cs"] = 1,
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/Golden/ExecutionIslandOrderTests.cs"] = 3,
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/Golden/GpuPassFusionFeature003RegressionTests.cs"] = 1,
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/Golden/GpuPassFusionScaleRegionTests.cs"] = 7,
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/Golden/LosslessCompositeCoverageTests.cs"] = 1,
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/Golden/ShaderMatrixUniformTests.cs"] = 1,
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/Golden/TargetCaptureValueWrapperTests.cs"] = 2,
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/Golden/WholeSourceFragmentOriginTests.cs"] = 1,
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/GraphicsContext2DTests.cs"] = 3,
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/HitTestDomainAgreementTests.cs"] = 2,
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/ImageSourceRenderNodeTest.cs"] = 1,
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/EffectItemTypedSuffixExecutionTests.cs"] = 1,
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/MovingOpaqueSourceBoundsTests.cs"] = 1,
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/NodeCacheScaleTests.cs"] = 3,
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/NodeCapturingExecutionCallbackTests.cs"] = 3,
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/NodeCapturingMetadataCallbackTests.cs"] = 3,
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/Planning/BackdropOrderingTests.cs"] = 1,
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/Planning/MaterializedInputCompositeTests.cs"] = 1,
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/Planning/ProductionResourceLifetimeTests.cs"] = 2,
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/Planning/RawScopeNestingAndCaptureOffsetTests.cs"] = 3,
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/Planning/RegionAnalysisReuseTests.cs"] = 1,
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/Planning/RegionAnalyzerTests.cs"] = 1,
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/Planning/RendererWideRecordingTests.cs"] = 7,
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/Planning/SymbolicOwningDomainTests.cs"] = 3,
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/Planning/SymbolicSupplyMappingTests.cs"] = 1,
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/Planning/TargetScopeLoweringTests.cs"] = 9,
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/Recording/DeclaredResourceOrderTests.cs"] = 1,
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/Recording/NodeRecordingTransactionTests.cs"] = 2,
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/Recording/RawSessionSlotResourceTests.cs"] = 1,
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/Recording/RecordingBufferPoolingTests.cs"] = 2,
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/Recording/RecordingGateFingerprintTests.cs"] = 5,
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/Recording/RecordingIdentityCollisionTests.cs"] = 1,
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/Recording/RecordingPerVisitAllocationTests.cs"] = 1,
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/Recording/RecordingSideEffectTests.cs"] = 1,
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/Recording/RenderNodeRecordingCacheTests.cs"] = 7,
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/Recording/RenderRecordingCrossCheckTests.cs"] = 9,
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/Recording/ValueReplaySafetyTests.cs"] = 2,
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/RectClipRenderNodeTest.cs"] = 2,
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/RenderNodeHasChangesTests.cs"] = 1,
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/RenderNodeRendererAllocationFailureTests.cs"] = 1,
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/RenderNodeRendererDeviceBoundsTests.cs"] = 1,
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/RenderNodeRendererExceptionSafetyTests.cs"] = 2,
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/RenderNodeRendererSnapshotFastPathTests.cs"] = 1,
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/RendererExceptionSafetyTests.cs"] = 1,
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/ResolutionScaleTests.cs"] = 1,
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/SlotBackedHitTestTests.cs"] = 5,
            ["tests/Beutl.UnitTests/Engine/Graphics/Rendering/SourceEffectiveScaleFlowTests.cs"] = 13,
            ["tests/Beutl.UnitTests/NodeGraph/ConfigureNodeOwnershipTests.cs"] = 2,
            ["tests/Beutl.UnitTests/NodeGraph/NodeGraphFilterEffectRenderNodeTests.cs"] = 6,
            ["tests/Beutl.UnitTests/ProjectSystem/SceneDrawableScaleTests.cs"] = 1,
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
            AssertAllOverridesAreMapped(overrides);
            AssertBaselineInventory("production", 31, s_productionOverrideBaseline, overrides);
            AssertBaselineInventory("test", 264, s_testOverrideBaseline, overrides);
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
        string processorType = $"Beutl.Graphics.Rendering.{BuildName("Render", "Node", "Processor")}";
        string[] pullNames =
        [
            BuildName("Pu", "ll"),
            BuildName("Pu", "ll", "To", "Root"),
        ];

        AssertNoFindings("Processor pull APIs must be absent.",
            s_corpus.Value.FindMembersDeclaredByType(processorType, pullNames));
    }

    [Test]
    public void ProcessorPullApiCensus_OnlyReportsMembersDeclaredByRenderNodeProcessor()
    {
        const string processorPath = "src/Beutl.Engine/Graphics/Rendering/RenderNodeProcessor.cs";
        SourceCorpus corpus = SourceCorpus.Create(
            (processorPath,
                """
                namespace Beutl.Graphics.Rendering;

                public sealed class RenderNodeProcessor
                {
                    public void Pull() { }

                    public void PullToRoot() { }
                }
                """),
            ("src/Other/RenderNodeProcessor.cs",
                """
                namespace Other;

                public sealed class RenderNodeProcessor
                {
                    public void Pull() { }
                }
                """),
            ("src/Beutl.Editor.Components/VersionControlTab/ViewModels/VersionControlPrimaryAction.cs",
                """
                namespace Beutl.Editor.Components.VersionControlTab.ViewModels;

                public enum VersionControlPrimaryActionKind
                {
                    Pull,
                }

                public sealed class VersionControlTabViewModel
                {
                    public void Pull() { }
                }
                """));

        SourceFinding[] findings = corpus.FindMembersDeclaredByType(
                "Beutl.Graphics.Rendering.RenderNodeProcessor",
                ["Pull", "PullToRoot"])
            .ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(findings, Has.Length.EqualTo(2));
            Assert.That(findings.Select(finding => finding.RelativePath),
                Is.All.EqualTo(processorPath));
            Assert.That(findings.Select(finding => finding.Snippet),
                Is.EquivalentTo(new[] { "public void Pull() { }", "public void PullToRoot() { }" }));
        }
    }

    [Test]
    public void ProcessorPullApiCensus_ReportsApplicableExtensionMethods()
    {
        const string extensionPath = "src/Compatibility/RenderNodeProcessorExtensions.cs";
        SourceCorpus corpus = SourceCorpus.Create(
            ("src/Beutl.Engine/Graphics/Rendering/RenderNodeProcessor.cs",
                """
                namespace Beutl.Graphics.Rendering;

                public sealed class RenderNodeProcessor
                {
                }
                """),
            (extensionPath,
                """
                using Beutl.Graphics.Rendering;

                namespace Compatibility;

                public static class RenderNodeProcessorExtensions
                {
                    public static void Pull(this RenderNodeProcessor processor) { }

                    public static void PullToRoot(
                        this global::Beutl.Graphics.Rendering.RenderNodeProcessor processor) { }
                }
                """),
            ("src/Other/OtherExtensions.cs",
                """
                namespace Other;

                public sealed class RenderNodeProcessor
                {
                }

                public static class OtherExtensions
                {
                    public static void Pull(this RenderNodeProcessor processor) { }
                }
                """));

        SourceFinding[] findings = corpus.FindMembersDeclaredByType(
                "Beutl.Graphics.Rendering.RenderNodeProcessor",
                ["Pull", "PullToRoot"])
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(findings, Has.Length.EqualTo(2));
            Assert.That(findings.Select(finding => finding.RelativePath),
                Is.All.EqualTo(extensionPath));
            Assert.That(findings.Select(finding => finding.Snippet),
                Is.EquivalentTo(new[]
                {
                    "public static void Pull(this RenderNodeProcessor processor) { }",
                    "public static void PullToRoot(",
                }));
        });
    }

    [Test]
    public void ProcessorPullApiCensus_Resolves_global_alias_block_and_shadowing_rules()
    {
        const string globalPath = "src/Compatibility/GlobalExtensions.cs";
        const string aliasPath = "src/Compatibility/AliasExtensions.cs";
        const string blockPath = "src/Compatibility/ExtensionBlock.cs";
        const string constrainedPath = "src/Compatibility/ConstrainedExtensions.cs";
        const string relativeImportPath = "src/Beutl.Compatibility/RelativeExtensions.cs";
        const string fileLocalPeerPath = "src/FileLocal/PeerExtensions.cs";
        SourceCorpus corpus = SourceCorpus.Create(
            ("src/Beutl.Engine/Graphics/Rendering/RenderNodeProcessor.cs",
                """
                namespace Beutl.Graphics.Rendering;
                public sealed class RenderNodeProcessor { }
                """),
            ("src/Compatibility/GlobalUsings.cs",
                "global using Beutl.Graphics.Rendering;"),
            (globalPath,
                """
                namespace Compatibility;
                public static class GlobalExtensions
                {
                    public static void Pull(this RenderNodeProcessor processor) { }
                }
                """),
            (aliasPath,
                """
                using Rendering = Beutl.Graphics.Rendering;
                namespace Compatibility;
                public static class AliasExtensions
                {
                    public static void Pull(this Rendering.RenderNodeProcessor processor) { }
                }
                """),
            (blockPath,
                """
                using Beutl.Graphics.Rendering;
                namespace Compatibility;
                public static class BlockExtensions
                {
                    extension(RenderNodeProcessor? processor)
                    {
                        public void Pull(RenderNode node) { }
                    }
                }
                """),
            (constrainedPath,
                """
                using Beutl.Graphics.Rendering;
                namespace Compatibility;
                public static class ConstrainedExtensions
                {
                    public static void Pull<T>(this T processor) where T : RenderNodeProcessor { }
                }
                """),
            (relativeImportPath,
                """
                namespace Beutl.Compatibility
                {
                    using Graphics.Rendering;
                    public static class RelativeExtensions
                    {
                        public static void Pull(this RenderNodeProcessor processor) { }
                    }
                }
                """),
            ("src/FileLocal/Shadow.cs",
                """
                namespace FileLocal;
                file sealed class RenderNodeProcessor { }
                """),
            (fileLocalPeerPath,
                """
                using Beutl.Graphics.Rendering;
                namespace FileLocal;
                public static class PeerExtensions
                {
                    public static void Pull(this RenderNodeProcessor processor) { }
                }
                """),
            ("src/Beutl.Engine/Graphics/Rendering/GenericProcessor.cs",
                """
                namespace Beutl.Graphics.Rendering;
                public sealed class RenderNodeProcessor<T>
                {
                    public void Pull() { }
                }
                """),
            ("src/Other/AliasShadow.cs",
                """
                using RenderNodeProcessor = Other.RenderNodeProcessor;
                using Beutl.Graphics.Rendering;
                namespace Other;
                public sealed class RenderNodeProcessor { }
                public static class AliasShadowExtensions
                {
                    public static void Pull(this RenderNodeProcessor processor) { }
                }
                """),
            ("src/Other/LocalShadow.cs",
                """
                using Beutl.Graphics.Rendering;
                namespace LocalShadow;
                public sealed class RenderNodeProcessor { }
                public static class LocalShadowExtensions
                {
                    public static void Pull(this RenderNodeProcessor processor) { }
                }
                """));

        SourceFinding[] findings = corpus.FindMembersDeclaredByType(
                "Beutl.Graphics.Rendering.RenderNodeProcessor",
                ["Pull"])
            .ToArray();

        Assert.That(
            findings.Select(finding => finding.RelativePath),
            Is.EquivalentTo(new[]
            {
                globalPath,
                aliasPath,
                blockPath,
                constrainedPath,
                relativeImportPath,
                fileLocalPeerPath,
            }));
    }

    [Test]
    public void ProcessorPullApiCensus_Includes_inherited_members()
    {
        const string basePath = "src/Beutl.Engine/Graphics/Rendering/ProcessorBase.cs";
        SourceCorpus corpus = SourceCorpus.Create(
            (basePath,
                """
                namespace Beutl.Graphics.Rendering;
                public class ProcessorBase
                {
                    public void Pull() { }
                }
                """),
            ("src/Beutl.Engine/Graphics/Rendering/RenderNodeProcessor.cs",
                """
                namespace Beutl.Graphics.Rendering;
                public sealed class RenderNodeProcessor : ProcessorBase { }
                """));

        SourceFinding[] findings = corpus.FindMembersDeclaredByType(
                "Beutl.Graphics.Rendering.RenderNodeProcessor",
                ["Pull"])
            .ToArray();

        Assert.That(findings.Select(finding => finding.RelativePath), Is.EqualTo(new[] { basePath }));
    }

    [Test]
    public void ProcessorPullApiCensus_Scopes_global_usings_to_their_compilation()
    {
        const string visiblePath = "src/A/VisibleExtensions.cs";
        SourceCorpus corpus = SourceCorpus.CreatePartitioned(
            ("A", "src/A/GlobalUsings.cs", "global using Beutl.Graphics.Rendering;"),
            ("A", visiblePath,
                """
                namespace A;
                public static class VisibleExtensions
                {
                    public static void Pull(this RenderNodeProcessor processor) { }
                }
                """),
            ("B", "src/B/InvisibleExtensions.cs",
                """
                namespace B;
                public static class InvisibleExtensions
                {
                    public static void Pull(this RenderNodeProcessor processor) { }
                }
                """),
            ("Engine", "src/Engine/RenderNodeProcessor.cs",
                """
                namespace Beutl.Graphics.Rendering;
                public sealed class RenderNodeProcessor { }
                """));

        SourceFinding[] findings = corpus.FindMembersDeclaredByType(
                "Beutl.Graphics.Rendering.RenderNodeProcessor",
                ["Pull"])
            .ToArray();

        Assert.That(findings.Select(finding => finding.RelativePath), Is.EqualTo(new[] { visiblePath }));
    }

    [Test]
    public void ListRasterizationCompatibility_IsRemoved()
    {
        string[] compatibilityNames =
        [
            BuildName("Rasterize", "To", "Render", "Targets"),
            BuildName("Rasterize", "And", "Concat"),
        ];
        IEnumerable<SourceFinding> findings = s_corpus.Value.FindEffectItemRasterizers()
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
        string qualifiedContextType = $"Beutl.Graphics.Rendering.{contextType}";
        string[] helperNames =
        [
            BuildName("Max", "Buffer", "Dimension"),
            BuildName("Sanitize", "Max", "Working", "Scale"),
            BuildName("Resolve", "Working", "Scale"),
            BuildName("Clamp", "Working", "Scale", "To", "Buffer", "Budget"),
        ];
        IEnumerable<SourceFinding> findings = s_corpus.Value.FindQualifiedReferences(contextType, helperNames)
            .Concat(s_corpus.Value.FindMembersDeclaredByType(qualifiedContextType, helperNames));

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

    private static void AssertAllOverridesAreMapped(IReadOnlyList<SourceMethod> allOverrides)
    {
        var mappedPaths = new HashSet<string>(s_productionOverrideBaseline.Keys, StringComparer.Ordinal);
        mappedPaths.UnionWith(s_testOverrideBaseline.Keys);
        SourceMethod[] unmapped = allOverrides
            .Where(sourceMethod => !mappedPaths.Contains(sourceMethod.Document.RelativePath))
            .ToArray();

        Assert.That(unmapped, Is.Empty,
            $"Every RenderNode.Process override must appear in the production or test inventory."
            + $"{Environment.NewLine}{FormatMethods(unmapped)}");
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
        private readonly IReadOnlyDictionary<string, UsingDirectiveSyntax[]> _globalUsings;
        private readonly DeclaredType[] _declaredTypes;

        private SourceCorpus(string repositoryRoot, IReadOnlyList<SourceDocument> documents)
        {
            RepositoryRoot = repositoryRoot;
            Documents = documents;
            _globalUsings = documents
                .GroupBy(document => document.CompilationId, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.SelectMany(document => document.Root.Usings)
                        .Where(item => item.GlobalKeyword.IsKind(SyntaxKind.GlobalKeyword))
                        .ToArray(),
                    StringComparer.Ordinal);
            _declaredTypes = documents
                .SelectMany(document => document.Root.DescendantNodes()
                    .OfType<TypeDeclarationSyntax>()
                    .Select(type => new DeclaredType(
                        document,
                        type,
                        GetQualifiedTypeName(type),
                        type.Modifiers.Any(SyntaxKind.FileKeyword))))
                .ToArray();
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
            var compilationIds = new Dictionary<string, string>(StringComparer.Ordinal);
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
                        CSharpParseOptions.Default
                            .WithLanguageVersion(LanguageVersion.Preview)
                            .WithDocumentationMode(DocumentationMode.Parse),
                        relativePath);
                    documents.Add(new SourceDocument(
                        relativePath,
                        text,
                        tree.GetCompilationUnitRoot(),
                        GetCompilationId(repositoryRoot, path, compilationIds)));
                }
            }

            documents.Sort((left, right) =>
                StringComparer.Ordinal.Compare(left.RelativePath, right.RelativePath));
            return new SourceCorpus(repositoryRoot, documents);
        }

        public static SourceCorpus Create(params (string RelativePath, string Source)[] sources)
        {
            SourceDocument[] documents = sources
                .Select(source =>
                {
                    SourceText text = SourceText.From(source.Source);
                    var tree = CSharpSyntaxTree.ParseText(
                        text,
                        CSharpParseOptions.Default
                            .WithLanguageVersion(LanguageVersion.Preview)
                            .WithDocumentationMode(DocumentationMode.Parse),
                        source.RelativePath);
                    return new SourceDocument(
                        source.RelativePath,
                        text,
                        tree.GetCompilationUnitRoot(),
                        "synthetic");
                })
                .OrderBy(document => document.RelativePath, StringComparer.Ordinal)
                .ToArray();
            return new SourceCorpus("synthetic", documents);
        }

        public static SourceCorpus CreatePartitioned(
            params (string CompilationId, string RelativePath, string Source)[] sources)
        {
            SourceDocument[] documents = sources
                .Select(source =>
                {
                    SourceText text = SourceText.From(source.Source);
                    var tree = CSharpSyntaxTree.ParseText(
                        text,
                        CSharpParseOptions.Default
                            .WithLanguageVersion(LanguageVersion.Preview)
                            .WithDocumentationMode(DocumentationMode.Parse),
                        source.RelativePath);
                    return new SourceDocument(
                        source.RelativePath,
                        text,
                        tree.GetCompilationUnitRoot(),
                        source.CompilationId);
                })
                .OrderBy(document => document.RelativePath, StringComparer.Ordinal)
                .ToArray();
            return new SourceCorpus("synthetic", documents);
        }

        private static string GetCompilationId(
            string repositoryRoot,
            string path,
            Dictionary<string, string> cache)
        {
            DirectoryInfo? directory = new FileInfo(path).Directory;
            var visited = new List<string>();
            while (directory is not null
                   && directory.FullName.StartsWith(repositoryRoot, StringComparison.Ordinal))
            {
                if (cache.TryGetValue(directory.FullName, out string? cached))
                {
                    foreach (string visitedDirectory in visited)
                    {
                        cache[visitedDirectory] = cached;
                    }

                    return cached;
                }

                visited.Add(directory.FullName);
                string? project = Directory.EnumerateFiles(
                        directory.FullName,
                        "*.csproj",
                        SearchOption.TopDirectoryOnly)
                    .OrderBy(candidate => candidate, StringComparer.Ordinal)
                    .FirstOrDefault();
                if (project is not null)
                {
                    string compilationId = NormalizePath(
                        Path.GetRelativePath(repositoryRoot, project));
                    foreach (string visitedDirectory in visited)
                    {
                        cache[visitedDirectory] = compilationId;
                    }

                    return compilationId;
                }

                directory = directory.Parent;
            }

            return NormalizePath(Path.GetRelativePath(repositoryRoot, path));
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

        public IEnumerable<SourceFinding> FindEffectItemRasterizers()
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
            string qualifiedTypeName,
            IReadOnlyCollection<string> memberNames)
        {
            int separator = qualifiedTypeName.LastIndexOf('.');
            string namespaceName = separator >= 0 ? qualifiedTypeName[..separator] : string.Empty;
            string typeName = separator >= 0 ? qualifiedTypeName[(separator + 1)..] : qualifiedTypeName;
            var memberNameSet = new HashSet<string>(memberNames, StringComparer.Ordinal);
            var reportedMembers = new HashSet<(string Path, int Position)>();
            foreach (SourceDocument document in Documents)
            {
                foreach (DeclaredType declared in _declaredTypes.Where(item =>
                             ReferenceEquals(item.Document, document)
                             && !item.IsFileLocal
                             && item.QualifiedName == qualifiedTypeName))
                {
                    foreach (DeclaredType visibleType in EnumerateTypeHierarchy(declared))
                    {
                        foreach (MemberDeclarationSyntax member in visibleType.Syntax.Members)
                        {
                            foreach (SyntaxToken identifier in GetDeclaredIdentifiers(member)
                                         .Where(token => memberNameSet.Contains(token.ValueText)))
                            {
                                if (reportedMembers.Add((visibleType.Document.RelativePath, identifier.SpanStart)))
                                {
                                    yield return visibleType.Document.ToFinding(identifier,
                                        $"forwarding member '{identifier.ValueText}' on '{typeName}'");
                                }
                            }
                        }
                    }
                }

                foreach (MethodDeclarationSyntax method in document.Root.DescendantNodes()
                             .OfType<MethodDeclarationSyntax>()
                             .Where(method => memberNameSet.Contains(method.Identifier.ValueText)))
                {
                    SyntaxNode? extensionBlock = method.Ancestors().FirstOrDefault(node =>
                        node.IsKind(SyntaxKind.ExtensionBlockDeclaration));
                    bool extensionBlockReceiver = extensionBlock is not null;
                    ParameterSyntax? receiver;
                    if (extensionBlock is not null)
                    {
                        receiver = extensionBlock.ChildNodes()
                            .OfType<ParameterListSyntax>()
                            .SelectMany(list => list.Parameters)
                            .FirstOrDefault();
                    }
                    else
                    {
                        receiver = method.ParameterList.Parameters.FirstOrDefault();
                    }

                    if (receiver is null
                        || !extensionBlockReceiver
                        && !receiver.Modifiers.Any(SyntaxKind.ThisKeyword)
                        || !CouldReferToType(
                            receiver.Type,
                            document,
                            namespaceName,
                            typeName,
                            qualifiedTypeName))
                    {
                        continue;
                    }

                    yield return document.ToFinding(
                        method.Identifier,
                        $"extension member '{method.Identifier.ValueText}' for '{qualifiedTypeName}'");
                }
            }
        }

        private static string GetQualifiedTypeName(TypeDeclarationSyntax type)
        {
            IEnumerable<string> namespaces = type.Ancestors()
                .OfType<BaseNamespaceDeclarationSyntax>()
                .Reverse()
                .Select(item => item.Name.ToString());
            IEnumerable<string> containingTypes = type.Ancestors()
                .OfType<TypeDeclarationSyntax>()
                .Reverse()
                .Select(GetTypeIdentityName);
            return string.Join('.', namespaces.Concat(containingTypes).Append(GetTypeIdentityName(type)));
        }

        private static string GetTypeIdentityName(TypeDeclarationSyntax type)
        {
            int arity = type.TypeParameterList?.Parameters.Count ?? 0;
            return arity == 0
                ? type.Identifier.ValueText
                : $"{type.Identifier.ValueText}`{arity}";
        }

        private IEnumerable<DeclaredType> EnumerateTypeHierarchy(DeclaredType root)
        {
            var pending = new Stack<DeclaredType>();
            var visited = new HashSet<(string Path, int Position)>();
            pending.Push(root);
            while (pending.TryPop(out DeclaredType? current))
            {
                if (!visited.Add((current.Document.RelativePath, current.Syntax.SpanStart)))
                {
                    continue;
                }

                yield return current;
                if (current.Syntax.BaseList is null)
                {
                    continue;
                }

                foreach (BaseTypeSyntax baseType in current.Syntax.BaseList.Types)
                {
                    foreach (DeclaredType candidate in _declaredTypes.Where(candidate =>
                                 IsVisibleIn(candidate, current.Document)
                                 && CouldReferToType(
                                     baseType.Type,
                                     current.Document,
                                     GetNamespaceName(candidate.Syntax),
                                     GetTypeIdentityName(candidate.Syntax),
                                     candidate.QualifiedName)))
                    {
                        pending.Push(candidate);
                    }
                }
            }
        }

        private bool CouldReferToType(
            TypeSyntax? type,
            SourceDocument document,
            string namespaceName,
            string typeName,
            string qualifiedTypeName)
        {
            if (type is null)
            {
                return false;
            }

            if (type is NullableTypeSyntax nullable)
            {
                type = nullable.ElementType;
            }

            if (type is IdentifierNameSyntax typeParameter
                && type.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault() is { } method)
            {
                TypeParameterConstraintClauseSyntax? clause = method.ConstraintClauses.FirstOrDefault(
                    item => item.Name.Identifier.ValueText == typeParameter.Identifier.ValueText);
                if (clause is not null)
                {
                    return clause.Constraints
                        .OfType<TypeConstraintSyntax>()
                        .Any(constraint => CouldReferToType(
                            constraint.Type,
                            document,
                            namespaceName,
                            typeName,
                            qualifiedTypeName));
                }
            }

            string writtenType = GetWrittenTypeIdentity(type);
            if (writtenType == qualifiedTypeName)
            {
                return true;
            }

            UsingDirectiveSyntax[] usings = document.Root.Usings.Concat(
                    type.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().SelectMany(item => item.Usings))
                .ToArray();
            if (_globalUsings.TryGetValue(
                    document.CompilationId,
                    out UsingDirectiveSyntax[]? globalUsings))
            {
                usings = usings.Concat(globalUsings).ToArray();
            }

            int nameSeparator = writtenType.IndexOf('.');
            string aliasName = nameSeparator < 0 ? writtenType : writtenType[..nameSeparator];
            UsingDirectiveSyntax? alias = usings.FirstOrDefault(item =>
                item.Alias?.Name.Identifier.ValueText == aliasName);
            if (alias is not null)
            {
                string aliasTarget = alias.Name is null
                    ? string.Empty
                    : GetWrittenName(alias.Name);
                string suffix = nameSeparator < 0 ? string.Empty : writtenType[nameSeparator..];
                return GetImportCandidates(alias, aliasTarget)
                    .Any(candidate => candidate + suffix == qualifiedTypeName);
            }

            if (writtenType != typeName)
            {
                return false;
            }

            string declaredNamespace = GetNamespaceName(type);
            for (string scope = declaredNamespace; ;)
            {
                string declaredType = string.IsNullOrEmpty(scope)
                    ? typeName
                    : scope + "." + typeName;
                if (_declaredTypes.Any(item =>
                        IsVisibleIn(item, document)
                        && item.QualifiedName == declaredType))
                {
                    return declaredType == qualifiedTypeName;
                }

                int separator = scope.LastIndexOf('.');
                if (separator < 0)
                {
                    break;
                }

                scope = scope[..separator];
            }

            return declaredNamespace == namespaceName
                   || usings.Any(item => item.Alias is null
                       && item.Name is not null
                       && GetImportCandidates(item, GetWrittenName(item.Name))
                           .Contains(namespaceName, StringComparer.Ordinal));
        }

        private static string GetWrittenTypeIdentity(TypeSyntax type)
        {
            return type switch
            {
                IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
                GenericNameSyntax generic =>
                    $"{generic.Identifier.ValueText}`{generic.TypeArgumentList.Arguments.Count}",
                QualifiedNameSyntax qualified =>
                    $"{GetWrittenTypeIdentity(qualified.Left)}.{GetWrittenTypeIdentity(qualified.Right)}",
                AliasQualifiedNameSyntax alias =>
                    alias.Alias.Identifier.ValueText == "global"
                        ? GetWrittenTypeIdentity(alias.Name)
                        : $"{alias.Alias.Identifier.ValueText}.{GetWrittenTypeIdentity(alias.Name)}",
                _ => type.ToString().Replace("global::", string.Empty, StringComparison.Ordinal),
            };
        }

        private static string GetWrittenName(NameSyntax name)
        {
            return name.ToString().Replace("global::", string.Empty, StringComparison.Ordinal);
        }

        private static IEnumerable<string> GetImportCandidates(
            UsingDirectiveSyntax directive,
            string importedName)
        {
            if (directive.GlobalKeyword.IsKind(SyntaxKind.GlobalKeyword))
            {
                yield return importedName;
                yield break;
            }

            string scope = GetNamespaceName(directive);
            while (!string.IsNullOrEmpty(scope))
            {
                yield return scope + "." + importedName;
                int separator = scope.LastIndexOf('.');
                scope = separator < 0 ? string.Empty : scope[..separator];
            }

            yield return importedName;
        }

        private static string GetNamespaceName(SyntaxNode node)
        {
            return string.Join('.', node.Ancestors()
                .OfType<BaseNamespaceDeclarationSyntax>()
                .Reverse()
                .Select(item => item.Name.ToString()));
        }

        private static bool IsVisibleIn(DeclaredType declared, SourceDocument document)
        {
            return declared.Document.CompilationId == document.CompilationId
                   && (!declared.IsFileLocal
                       || declared.Document.RelativePath == document.RelativePath);
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
        CompilationUnitSyntax Root,
        string CompilationId)
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

    private sealed record DeclaredType(
        SourceDocument Document,
        TypeDeclarationSyntax Syntax,
        string QualifiedName,
        bool IsFileLocal);

    private sealed record SourceFinding(
        string RelativePath,
        int Line,
        string Detail,
        string Snippet);
}
