using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;

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
    public void ProcessorPullRuntimeSurface_IncludesGeneratedEngineMembers()
    {
        const string processorTypeName = "Beutl.Graphics.Rendering.RenderNodeProcessor";
        string[] pullNames = ["Pull", "PullToRoot"];
        Assembly engine = typeof(Beutl.Graphics.Rendering.RenderNode).Assembly;
        Type? processor = engine.GetType(processorTypeName);
        if (processor is null)
        {
            Assert.Pass();
            return;
        }

        MemberInfo[] directMembers = processor.GetMembers(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Where(member => pullNames.Contains(member.Name, StringComparer.Ordinal))
            .ToArray();
        MethodInfo[] extensionMembers = engine.GetTypes()
            .SelectMany(type => type.GetMethods(
                BindingFlags.Public | BindingFlags.Static))
            .Where(method =>
                pullNames.Contains(method.Name, StringComparer.Ordinal)
                && method.IsDefined(typeof(ExtensionAttribute), inherit: false)
                && method.GetParameters() is [{ ParameterType: var receiver }, ..]
                && (receiver.IsAssignableFrom(processor)
                    || receiver.IsGenericParameter
                    && receiver.GetGenericParameterConstraints()
                        .Any(constraint => constraint.IsAssignableFrom(processor))))
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(directMembers, Is.Empty);
            Assert.That(extensionMembers, Is.Empty);
        });
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
    public void ProcessorPullApiCensus_Resolves_target_symbols_and_receiver_conversions()
    {
        string[] expectedPaths =
        [
            "src/Compatibility/ConditionalExtensions.cs",
            "src/Compatibility/InnerAliasExtensions.cs",
            "src/Compatibility/GenericBlockExtensions.cs",
            "src/Compatibility/PropertyExtensions.cs",
            "src/Beutl.Compatibility/RelativeQualifiedExtensions.cs",
            "src/Compatibility/BaseReceiverExtensions.cs",
            "src/Compatibility/DebugExtensions.cs",
            "src/Compatibility/BuiltInExtensions.cs",
            "src/Compatibility/ImportedQualifiedExtensions.cs",
            "src/Conditional/ConditionalGlobalExtensions.cs",
        ];
        SourceCorpus corpus = SourceCorpus.Create(
            ("src/Beutl.Engine/Graphics/Rendering/RenderNodeProcessor.cs",
                """
                namespace Beutl.Graphics.Rendering;
                public class ProcessorBase { }
                public sealed class RenderNodeProcessor : ProcessorBase { }
                """),
            ("src/Other/RenderNodeProcessor.cs",
                """
                namespace Other;
                public sealed class RenderNodeProcessor { }
                """),
            (expectedPaths[0],
                """
                namespace Compatibility;
                public static class ConditionalExtensions
                {
                #if WINDOWS
                    public static void Pull(
                        this Beutl.Graphics.Rendering.RenderNodeProcessor processor) { }
                #endif
                }
                """),
            (expectedPaths[1],
                """
                using P = Other.RenderNodeProcessor;
                namespace Compatibility
                {
                    using P = Beutl.Graphics.Rendering.RenderNodeProcessor;
                    public static class InnerAliasExtensions
                    {
                        public static void Pull(this P processor) { }
                    }
                }
                """),
            (expectedPaths[2],
                """
                using Beutl.Graphics.Rendering;
                namespace Compatibility;
                public static class GenericBlockExtensions
                {
                    extension<T>(T processor) where T : RenderNodeProcessor
                    {
                        public void Pull() { }
                    }
                }
                """),
            (expectedPaths[3],
                """
                using System;
                using Beutl.Graphics.Rendering;
                namespace Compatibility;
                public static class PropertyExtensions
                {
                    extension(RenderNodeProcessor processor)
                    {
                        public Action Pull => () => { };
                    }
                }
                """),
            (expectedPaths[4],
                """
                namespace Beutl.Compatibility;
                public static class RelativeQualifiedExtensions
                {
                    public static void Pull(
                        this Graphics.Rendering.RenderNodeProcessor processor) { }
                }
                """),
            (expectedPaths[5],
                """
                using Beutl.Graphics.Rendering;
                namespace Compatibility;
                public static class BaseReceiverExtensions
                {
                    public static void Pull(this ProcessorBase processor) { }
                }
                """),
            (expectedPaths[6],
                """
                namespace Compatibility;
                public static class DebugExtensions
                {
                #if DEBUG
                    public static void Pull(
                        this Beutl.Graphics.Rendering.RenderNodeProcessor processor) { }
                #endif
                }
                """),
            (expectedPaths[7],
                """
                namespace Compatibility;
                public static class BuiltInExtensions
                {
                #if FFMPEG_BUILD_IN
                    public static void Pull(
                        this Beutl.Graphics.Rendering.RenderNodeProcessor processor) { }
                #endif
                }
                """),
            (expectedPaths[8],
                """
                using Beutl.Graphics;
                namespace Compatibility;
                public static class ImportedQualifiedExtensions
                {
                    public static void Pull(this Rendering.RenderNodeProcessor processor) { }
                }
                """),
            ("src/Conditional/GlobalUsings.cs",
                """
                #if WINDOWS
                global using Beutl.Graphics.Rendering;
                #endif
                """),
            (expectedPaths[9],
                """
                namespace Conditional;
                public static class ConditionalGlobalExtensions
                {
                #if WINDOWS
                    public static void Pull(this RenderNodeProcessor processor) { }
                #endif
                }
                """));

        SourceFinding[] findings = corpus.FindMembersDeclaredByType(
                "Beutl.Graphics.Rendering.RenderNodeProcessor",
                ["Pull"])
            .ToArray();

        Assert.That(
            findings.Select(finding => finding.RelativePath),
            Is.EquivalentTo(expectedPaths));
    }

    [Test]
    public void ProcessorPullApiCensus_Reports_positional_record_members()
    {
        SourceCorpus corpus = SourceCorpus.Create(
            ("src/Beutl.Engine/Graphics/Rendering/RenderNodeProcessor.cs",
                """
                using System;
                namespace Beutl.Graphics.Rendering;
                public sealed record RenderNodeProcessor(Action Pull);
                """));

        SourceFinding[] findings = corpus.FindMembersDeclaredByType(
                "Beutl.Graphics.Rendering.RenderNodeProcessor",
                ["Pull"])
            .ToArray();

        Assert.That(findings, Has.Length.EqualTo(1));
    }

    [Test]
    public void ProcessorPullApiCensus_Excludes_test_compilations()
    {
        SourceCorpus corpus = SourceCorpus.Create(
            ("src/Beutl.Engine/Graphics/Rendering/RenderNodeProcessor.cs",
                """
                namespace Beutl.Graphics.Rendering;
                public sealed class RenderNodeProcessor { }
                """),
            ("tests/Compatibility/RenderNodeProcessor.cs",
                """
                namespace Beutl.Graphics.Rendering;
                public sealed class RenderNodeProcessor
                {
                    public void Pull() { }
                }
                """));

        Assert.That(
            corpus.FindMembersDeclaredByType(
                "Beutl.Graphics.Rendering.RenderNodeProcessor",
                ["Pull"]),
            Is.Empty);
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
    public void ProcessorPullApiCensus_Filters_private_bases_and_resolves_generic_aliases()
    {
        const string genericBasePath = "src/Compatibility/ProcessorBase.cs";
        SourceCorpus corpus = SourceCorpus.Create(
            (genericBasePath,
                """
                namespace Compatibility;
                public class ProcessorBase<T>
                {
                    public void Pull() { }
                    private void PullToRoot() { }
                }
                """),
            ("src/Beutl.Engine/Graphics/Rendering/RenderNodeProcessor.cs",
                """
                using B = Compatibility.ProcessorBase<int>;
                namespace Beutl.Graphics.Rendering;
                public sealed class RenderNodeProcessor : B { }
                """));

        SourceFinding[] findings = corpus.FindMembersDeclaredByType(
                "Beutl.Graphics.Rendering.RenderNodeProcessor",
                ["Pull", "PullToRoot"])
            .ToArray();

        Assert.That(findings.Select(finding => finding.RelativePath),
            Is.EqualTo(new[] { genericBasePath }));
    }

    [Test]
    public void ProcessorPullApiCensus_Preserves_global_qualification_over_aliases()
    {
        SourceCorpus corpus = SourceCorpus.Create(
            ("src/Beutl.Engine/Graphics/Rendering/RenderNodeProcessor.cs",
                """
                namespace Beutl.Graphics.Rendering;
                public sealed class RenderNodeProcessor { }
                """),
            ("src/Other/GlobalProcessor.cs",
                """
                namespace Rendering;
                public sealed class RenderNodeProcessor { }
                """),
            ("src/Compatibility/GlobalQualifiedExtensions.cs",
                """
                using Rendering = Beutl.Graphics.Rendering;
                namespace Compatibility;
                public static class GlobalQualifiedExtensions
                {
                    public static void Pull(
                        this global::Rendering.RenderNodeProcessor processor) { }
                }
                """));

        Assert.That(
            corpus.FindMembersDeclaredByType(
                "Beutl.Graphics.Rendering.RenderNodeProcessor",
                ["Pull"]),
            Is.Empty);
    }

    [Test]
    public void ProcessorPullApiCensus_Uses_nearest_imported_namespace()
    {
        SourceCorpus corpus = SourceCorpus.Create(
            ("src/Beutl.Engine/Graphics/Rendering/RenderNodeProcessor.cs",
                """
                namespace Beutl.Graphics.Rendering;
                public sealed class RenderNodeProcessor { }
                """),
            ("src/Compatibility/LocalProcessor.cs",
                """
                namespace Beutl.Compatibility.Graphics.Rendering;
                public sealed class RenderNodeProcessor { }
                """),
            ("src/Compatibility/LocalExtensions.cs",
                """
                namespace Beutl.Compatibility
                {
                    using Graphics.Rendering;
                    public static class LocalExtensions
                    {
                        public static void Pull(this RenderNodeProcessor processor) { }
                    }
                }
                """));

        Assert.That(
            corpus.FindMembersDeclaredByType(
                "Beutl.Graphics.Rendering.RenderNodeProcessor",
                ["Pull"]),
            Is.Empty);
    }

    [Test]
    public void ProcessorPullApiCensus_Includes_referenced_receiver_bases()
    {
        const string extensionPath = "src/Compatibility/FrameworkBaseExtensions.cs";
        SourceCorpus corpus = SourceCorpus.Create(
            ("src/Beutl.Engine/Graphics/Rendering/RenderNodeProcessor.cs",
                """
                using System;
                namespace Beutl.Graphics.Rendering;
                public sealed class RenderNodeProcessor : IDisposable
                {
                    public void Dispose() { }
                }
                """),
            (extensionPath,
                """
                using System;
                namespace Compatibility;
                public static class FrameworkBaseExtensions
                {
                    public static void Pull(this IDisposable processor) { }
                    public static void PullToRoot(this object processor) { }
                }
                """));

        Assert.That(
            corpus.FindMembersDeclaredByType(
                    "Beutl.Graphics.Rendering.RenderNodeProcessor",
                    ["Pull", "PullToRoot"])
                .Select(finding => finding.RelativePath),
            Is.EqualTo(new[] { extensionPath, extensionPath }));
    }

    [Test]
    public void ProcessorPullApiCensus_Combines_reachable_nested_directive_symbols()
    {
        const string extensionPath = "src/Compatibility/NestedConditionalExtensions.cs";
        SourceCorpus corpus = SourceCorpus.Create(
            ("src/Beutl.Engine/Graphics/Rendering/RenderNodeProcessor.cs",
                """
                namespace Beutl.Graphics.Rendering;
                public sealed class RenderNodeProcessor { }
                """),
            (extensionPath,
                """
                namespace Compatibility;
                public static class NestedConditionalExtensions
                {
                #if WINDOWS
                #elif FEATURE
                #if DEBUG
                    public static void Pull(
                        this Beutl.Graphics.Rendering.RenderNodeProcessor processor) { }
                #endif
                #endif
                }
                """));

        Assert.That(
            corpus.FindMembersDeclaredByType(
                    "Beutl.Graphics.Rendering.RenderNodeProcessor",
                    ["Pull"])
                .Select(finding => finding.RelativePath),
            Is.EqualTo(new[] { extensionPath }));
    }

    [Test]
    public void ProcessorPullApiCensus_Normalizes_escaped_namespace_identifiers()
    {
        SourceCorpus corpus = SourceCorpus.Create(
            ("src/Beutl.Engine/Graphics/Rendering/RenderNodeProcessor.cs",
                """
                namespace @Beutl.Graphics.Rendering;
                public sealed class RenderNodeProcessor
                {
                    public void Pull() { }
                }
                """));

        Assert.That(
            corpus.FindMembersDeclaredByType(
                "Beutl.Graphics.Rendering.RenderNodeProcessor",
                ["Pull"]),
            Has.Count.EqualTo(1));
    }

    [Test]
    public void ProcessorPullApiCensus_Does_not_inherit_interface_default_members()
    {
        SourceCorpus corpus = SourceCorpus.Create(
            ("src/Beutl.Engine/Graphics/Rendering/RenderNodeProcessor.cs",
                """
                namespace Beutl.Graphics.Rendering;
                public interface IProcessor
                {
                    public void Pull() { }
                }
                public sealed class RenderNodeProcessor : IProcessor { }
                """));

        Assert.That(
            corpus.FindMembersDeclaredByType(
                "Beutl.Graphics.Rendering.RenderNodeProcessor",
                ["Pull"]),
            Is.Empty);
    }

    [Test]
    public void ProcessorPullApiCensus_Resolves_engine_extern_alias_receivers()
    {
        const string extensionPath = "src/Compatibility/ExternAliasExtensions.cs";
        SourceCorpus corpus = SourceCorpus.Create(
            ("src/Beutl.Engine/Graphics/Rendering/RenderNodeProcessor.cs",
                """
                namespace Beutl.Graphics.Rendering;
                public sealed class RenderNodeProcessor { }
                """),
            (extensionPath,
                """
                extern alias Engine;
                namespace Compatibility;
                public static class ExternAliasExtensions
                {
                    public static void Pull(
                        this Engine::Beutl.Graphics.Rendering.RenderNodeProcessor processor) { }
                }
                """));

        Assert.That(
            corpus.FindMembersDeclaredByType(
                    "Beutl.Graphics.Rendering.RenderNodeProcessor",
                    ["Pull"])
                .Select(finding => finding.RelativePath),
            Is.EqualTo(new[] { extensionPath }));
    }

    [Test]
    public void ProcessorPullApiCensus_Uses_active_variant_receiver_hierarchy()
    {
        const string extensionPath = "src/Compatibility/WindowsBaseExtensions.cs";
        SourceCorpus corpus = SourceCorpus.Create(
            ("src/Beutl.Engine/Graphics/Rendering/RenderNodeProcessor.cs",
                """
                namespace Beutl.Graphics.Rendering;
                public interface IProcessorBase { }
                public sealed class RenderNodeProcessor
                #if WINDOWS
                    : IProcessorBase
                #endif
                { }
                """),
            (extensionPath,
                """
                using Beutl.Graphics.Rendering;
                namespace Compatibility;
                public static class WindowsBaseExtensions
                {
                #if WINDOWS
                    public static void Pull(this IProcessorBase processor) { }
                #endif
                }
                """));

        Assert.That(
            corpus.FindMembersDeclaredByType(
                    "Beutl.Graphics.Rendering.RenderNodeProcessor",
                    ["Pull"])
                .Select(finding => finding.RelativePath),
            Is.EqualTo(new[] { extensionPath }));
    }

    [Test]
    public void ProcessorPullApiCensus_Follows_indirect_referenced_base_receivers()
    {
        const string extensionPath = "src/Compatibility/IndirectBaseExtensions.cs";
        SourceCorpus corpus = SourceCorpus.Create(
            ("src/Beutl.Engine/Graphics/Rendering/RenderNodeProcessor.cs",
                """
                using System;
                namespace Beutl.Graphics.Rendering;
                public class ProcessorBase : IDisposable
                {
                    public void Dispose() { }
                }
                public sealed class RenderNodeProcessor : ProcessorBase { }
                """),
            (extensionPath,
                """
                using System;
                namespace Compatibility;
                public static class IndirectBaseExtensions
                {
                    public static void Pull(this IDisposable processor) { }
                }
                """));

        Assert.That(
            corpus.FindMembersDeclaredByType(
                    "Beutl.Graphics.Rendering.RenderNodeProcessor",
                    ["Pull"])
                .Select(finding => finding.RelativePath),
            Is.EqualTo(new[] { extensionPath }));
    }

    [Test]
    public void ProcessorPullApiCensus_Applies_hierarchy_to_generic_receiver_constraints()
    {
        const string extensionPath = "src/Compatibility/ConstrainedBaseExtensions.cs";
        SourceCorpus corpus = SourceCorpus.Create(
            ("src/Beutl.Engine/Graphics/Rendering/RenderNodeProcessor.cs",
                """
                namespace Beutl.Graphics.Rendering;
                public class ProcessorBase { }
                public sealed class RenderNodeProcessor : ProcessorBase { }
                """),
            (extensionPath,
                """
                using Beutl.Graphics.Rendering;
                namespace Compatibility;
                public static class ConstrainedBaseExtensions
                {
                    public static void Pull<T>(this T processor) where T : ProcessorBase { }
                }
                """));

        Assert.That(
            corpus.FindMembersDeclaredByType(
                    "Beutl.Graphics.Rendering.RenderNodeProcessor",
                    ["Pull"])
                .Select(finding => finding.RelativePath),
            Is.EqualTo(new[] { extensionPath }));
    }

    [Test]
    public void ProcessorPullApiCensus_Distinguishes_referenced_base_identities()
    {
        SourceCorpus corpus = SourceCorpus.Create(
            ("src/Beutl.Engine/Graphics/Rendering/RenderNodeProcessor.cs",
                """
                namespace Other
                {
                    public interface IProcessor { }
                }
                namespace Beutl.Graphics.Rendering
                {
                    public sealed class RenderNodeProcessor : Other.IProcessor { }
                }
                """),
            ("src/Compatibility/CompatibilityProcessor.cs",
                """
                namespace Compatibility
                {
                    public interface IProcessor { }
                    public static class CompatibilityExtensions
                    {
                        public static void Pull(this IProcessor processor) { }
                    }
                }
                """));

        Assert.That(
            corpus.FindMembersDeclaredByType(
                "Beutl.Graphics.Rendering.RenderNodeProcessor",
                ["Pull"]),
            Is.Empty);
    }

    [Test]
    public void ProcessorPullApiCensus_Stops_aliases_at_nearest_existing_type()
    {
        SourceCorpus corpus = SourceCorpus.Create(
            ("src/Beutl.Engine/Graphics/Rendering/RenderNodeProcessor.cs",
                """
                namespace Beutl.Graphics.Rendering;
                public sealed class RenderNodeProcessor { }
                """),
            ("src/Compatibility/ShadowedAliasExtensions.cs",
                """
                namespace Compatibility.Beutl.Graphics.Rendering;
                public sealed class RenderNodeProcessor { }

                namespace Compatibility
                {
                    using P = Beutl.Graphics.Rendering.RenderNodeProcessor;
                    public static class ShadowedAliasExtensions
                    {
                        public static void Pull(this P processor) { }
                    }
                }
                """));

        Assert.That(
            corpus.FindMembersDeclaredByType(
                "Beutl.Graphics.Rendering.RenderNodeProcessor",
                ["Pull"]),
            Is.Empty);
    }

    [Test]
    public void ProcessorPullApiCensus_Includes_protected_members()
    {
        const string basePath = "src/Beutl.Engine/Graphics/Rendering/ProcessorBase.cs";
        SourceCorpus corpus = SourceCorpus.Create(
            (basePath,
                """
                namespace Beutl.Graphics.Rendering;
                public class ProcessorBase
                {
                    protected void Pull() { }
                }
                """),
            ("src/Beutl.Engine/Graphics/Rendering/RenderNodeProcessor.cs",
                """
                namespace Beutl.Graphics.Rendering;
                public class RenderNodeProcessor : ProcessorBase { }
                """));

        Assert.That(
            corpus.FindMembersDeclaredByType(
                    "Beutl.Graphics.Rendering.RenderNodeProcessor",
                    ["Pull"])
                .Select(finding => finding.RelativePath),
            Is.EqualTo(new[] { basePath }));
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
        private readonly HashSet<string> _productionCompilationIds;
        private readonly Lazy<SourceDocument[]> _memberCensusDocuments;
        private readonly Lazy<IReadOnlyDictionary<(string CompilationId, string VariantId), UsingDirectiveSyntax[]>>
            _memberGlobalUsings;
        private readonly Lazy<DeclaredType[]> _memberDeclaredTypes;

        private SourceCorpus(string repositoryRoot, IReadOnlyList<SourceDocument> documents)
        {
            RepositoryRoot = repositoryRoot;
            Documents = documents;
            _productionCompilationIds = GetProductionCompilationIds(repositoryRoot, documents);
            _memberCensusDocuments = new Lazy<SourceDocument[]>(() =>
                CreateMemberCensusDocuments(
                    repositoryRoot,
                    documents,
                    _productionCompilationIds));
            _memberGlobalUsings = new Lazy<IReadOnlyDictionary<
                (string CompilationId, string VariantId),
                UsingDirectiveSyntax[]>>(() =>
                    _memberCensusDocuments.Value
                        .GroupBy(document =>
                            (document.CompilationId, document.VariantId))
                        .ToDictionary(
                            group => group.Key,
                            group => group.SelectMany(document => document.Root.Usings)
                                .Where(item => item.GlobalKeyword.IsKind(
                                    SyntaxKind.GlobalKeyword))
                                .ToArray()));
            _memberDeclaredTypes = new Lazy<DeclaredType[]>(() =>
                _memberCensusDocuments.Value
                    .SelectMany(document => document.Root.DescendantNodes()
                        .OfType<TypeDeclarationSyntax>()
                        .Select(type => new DeclaredType(
                            document,
                            type,
                            GetQualifiedTypeName(type),
                            type.Modifiers.Any(SyntaxKind.FileKeyword))))
                    .ToArray());
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
            foreach (SourceDocument document in EnumerateMemberCensusDocuments())
            {
                foreach (DeclaredType declared in document.Root.DescendantNodes()
                             .OfType<TypeDeclarationSyntax>()
                             .Select(type => new DeclaredType(
                                 document,
                                 type,
                                 GetQualifiedTypeName(type),
                                 type.Modifiers.Any(SyntaxKind.FileKeyword)))
                             .Where(item =>
                                 !item.IsFileLocal
                                 && item.QualifiedName == qualifiedTypeName))
                {
                    foreach (DeclaredType visibleType in EnumerateTypeHierarchy(declared))
                    {
                        if (!ReferenceEquals(visibleType.Syntax, declared.Syntax)
                            && visibleType.Syntax is InterfaceDeclarationSyntax)
                        {
                            continue;
                        }

                        foreach (MemberDeclarationSyntax member in visibleType.Syntax.Members)
                        {
                            if (!IsExternallyAccessible(member, visibleType.Syntax))
                            {
                                continue;
                            }

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

                        if (visibleType.Syntax is RecordDeclarationSyntax
                            {
                                ParameterList: { } parameterList,
                            })
                        {
                            foreach (SyntaxToken identifier in parameterList.Parameters
                                         .Select(parameter => parameter.Identifier)
                                         .Where(token => memberNameSet.Contains(token.ValueText)))
                            {
                                if (reportedMembers.Add((
                                        visibleType.Document.RelativePath,
                                        identifier.SpanStart)))
                                {
                                    yield return visibleType.Document.ToFinding(
                                        identifier,
                                        $"positional member '{identifier.ValueText}' on '{typeName}'");
                                }
                            }
                        }
                    }
                }

                foreach (MemberDeclarationSyntax member in document.Root.DescendantNodes()
                             .OfType<MemberDeclarationSyntax>())
                {
                    TypeDeclarationSyntax? containingType = member.Ancestors()
                        .OfType<TypeDeclarationSyntax>()
                        .FirstOrDefault();
                    if (containingType is null
                        || !IsExternallyAccessible(member, containingType))
                    {
                        continue;
                    }

                    SyntaxToken[] identifiers = GetDeclaredIdentifiers(member)
                        .Where(token => memberNameSet.Contains(token.ValueText))
                        .ToArray();
                    if (identifiers.Length == 0)
                    {
                        continue;
                    }

                    SyntaxNode? extensionBlock = member.Ancestors().FirstOrDefault(node =>
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
                    else if (member is MethodDeclarationSyntax method)
                    {
                        receiver = method.ParameterList.Parameters.FirstOrDefault();
                    }
                    else
                    {
                        continue;
                    }

                    if (receiver is null
                        || !extensionBlockReceiver
                        && !receiver.Modifiers.Any(SyntaxKind.ThisKeyword)
                        || !CanReceiveType(
                            receiver.Type,
                            document,
                            namespaceName,
                            typeName,
                            qualifiedTypeName))
                    {
                        continue;
                    }

                    foreach (SyntaxToken identifier in identifiers)
                    {
                        if (reportedMembers.Add((document.RelativePath, identifier.SpanStart)))
                        {
                            yield return document.ToFinding(
                                identifier,
                                $"extension member '{identifier.ValueText}' for '{qualifiedTypeName}'");
                        }
                    }
                }
            }
        }

        private IEnumerable<SourceDocument> EnumerateMemberCensusDocuments()
        {
            return _memberCensusDocuments.Value;
        }

        private static SourceDocument[] CreateMemberCensusDocuments(
            string repositoryRoot,
            IReadOnlyList<SourceDocument> documents,
            IReadOnlySet<string> productionCompilationIds)
        {
            var result = new List<SourceDocument>();
            IEnumerable<SourceDocument> memberDocuments = documents;
            if (repositoryRoot != "synthetic")
            {
                memberDocuments = memberDocuments.Concat(
                    GetLinkedSourceDocuments(repositoryRoot, documents));
            }

            foreach (IGrouping<string, SourceDocument> compilation in memberDocuments
                         .Where(document =>
                             document.RelativePath.StartsWith("src/", StringComparison.Ordinal)
                             && productionCompilationIds.Contains(document.CompilationId))
                         .GroupBy(document => document.CompilationId, StringComparer.Ordinal))
            {
                IReadOnlyList<string[]> symbolSets = GetCompilationSymbolSets(
                    repositoryRoot,
                    compilation.Key,
                    compilation);
                foreach (SourceDocument document in compilation)
                {
                    foreach (string[] symbols in symbolSets)
                    {
                        var tree = CSharpSyntaxTree.ParseText(
                            document.Text,
                            CSharpParseOptions.Default
                                .WithLanguageVersion(LanguageVersion.Preview)
                                .WithDocumentationMode(DocumentationMode.Parse)
                                .WithPreprocessorSymbols(symbols),
                            document.RelativePath);
                        result.Add(document with
                        {
                            Root = tree.GetCompilationUnitRoot(),
                            VariantId = string.Join(';', symbols),
                        });
                    }
                }
            }

            return [.. result];
        }

        private static IEnumerable<SourceDocument> GetLinkedSourceDocuments(
            string repositoryRoot,
            IReadOnlyList<SourceDocument> documents)
        {
            var byPath = documents
                .GroupBy(document => document.RelativePath, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            string sourceRoot = Path.Combine(repositoryRoot, "src");
            foreach (string projectPath in Directory.EnumerateFiles(
                         sourceRoot,
                         "*.csproj",
                         SearchOption.AllDirectories))
            {
                string compilationId = NormalizePath(Path.GetRelativePath(
                    repositoryRoot,
                    projectPath));
                XDocument project;
                try
                {
                    project = XDocument.Load(projectPath);
                }
                catch (Exception ex) when (ex is IOException
                                           or UnauthorizedAccessException
                                           or System.Xml.XmlException)
                {
                    continue;
                }

                foreach (string include in project.Descendants()
                             .Where(element => element.Name.LocalName == "Compile")
                             .Select(element => (string?)element.Attribute("Include"))
                             .OfType<string>()
                             .Where(include =>
                                 !include.Contains('*', StringComparison.Ordinal)
                                 && !include.Contains("$(", StringComparison.Ordinal)))
                {
                    string fullPath;
                    try
                    {
                        fullPath = Path.GetFullPath(
                            include.Replace('\\', Path.DirectorySeparatorChar),
                            Path.GetDirectoryName(projectPath)!);
                    }
                    catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
                    {
                        continue;
                    }

                    if (!File.Exists(fullPath))
                    {
                        continue;
                    }

                    string relativePath = NormalizePath(Path.GetRelativePath(
                        repositoryRoot,
                        fullPath));
                    if (!relativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                        || documents.Any(document =>
                            document.RelativePath == relativePath
                            && document.CompilationId == compilationId))
                    {
                        continue;
                    }

                    SourceText text = byPath.TryGetValue(relativePath, out SourceDocument? existing)
                        ? existing.Text
                        : SourceText.From(File.ReadAllText(fullPath));
                    var tree = CSharpSyntaxTree.ParseText(
                        text,
                        CSharpParseOptions.Default
                            .WithLanguageVersion(LanguageVersion.Preview)
                            .WithDocumentationMode(DocumentationMode.Parse),
                        relativePath);
                    yield return new SourceDocument(
                        relativePath,
                        text,
                        tree.GetCompilationUnitRoot(),
                        compilationId);
                }
            }
        }

        private static IReadOnlyList<string[]> GetCompilationSymbolSets(
            string repositoryRoot,
            string compilationId,
            IEnumerable<SourceDocument> documents)
        {
            string[] baseSymbols =
            [
                "NET",
                "NET10_0",
                "NET10_0_OR_GREATER",
                "NET9_0_OR_GREATER",
                "NET8_0_OR_GREATER",
                "NETCOREAPP",
            ];
            string[] windowsSymbols = ["NET10_0_WINDOWS", "WINDOWS"];
            var directiveSymbols = new HashSet<string>(StringComparer.Ordinal);
            var directivePositiveSets = new List<string[]>();
            var conditionalPattern = new Regex(
                @"(?m)^\s*#(?:if|elif)\s+(?<expression>[^\r\n]+)",
                RegexOptions.CultureInvariant);
            var identifierPattern = new Regex(
                @"\b[A-Za-z_][A-Za-z0-9_]*\b",
                RegexOptions.CultureInvariant);
            foreach (SourceDocument document in documents)
            {
                foreach (Match match in conditionalPattern.Matches(document.Text.ToString()))
                {
                    string expression = match.Groups["expression"].Value;
                    string withoutNegatedSymbols = Regex.Replace(
                        expression,
                        @"!\s*[A-Za-z_][A-Za-z0-9_]*",
                        string.Empty,
                        RegexOptions.CultureInvariant);
                    string[] positiveSymbols = identifierPattern.Matches(withoutNegatedSymbols)
                        .Select(item => item.Value)
                        .Where(IsPreprocessorSymbol)
                        .Distinct(StringComparer.Ordinal)
                        .ToArray();
                    if (positiveSymbols.Length > 0)
                    {
                        directivePositiveSets.Add(positiveSymbols);
                    }

                    foreach (string symbol in identifierPattern.Matches(expression)
                                 .Select(item => item.Value)
                                 .Where(IsPreprocessorSymbol))
                    {
                        directiveSymbols.Add(symbol);
                    }
                }
            }

            HashSet<string> projectSymbols = GetProjectDefinedSymbols(
                repositoryRoot,
                compilationId);
            var variants = new Dictionary<string, string[]>(StringComparer.Ordinal);
            AddSymbolVariant(variants, []);
            AddSymbolVariant(variants, baseSymbols);
            AddSymbolVariant(variants, baseSymbols.Concat(["DEBUG", "TRACE"]));
            AddSymbolVariant(variants, baseSymbols.Concat(windowsSymbols));
            AddSymbolVariant(
                variants,
                baseSymbols.Concat(windowsSymbols).Concat(["DEBUG", "TRACE"]));
            AddSymbolVariant(variants, baseSymbols.Concat(projectSymbols));
            AddSymbolVariant(
                variants,
                baseSymbols.Concat(projectSymbols).Concat(["DEBUG", "TRACE"]));
            AddSymbolVariant(
                variants,
                baseSymbols.Concat(windowsSymbols).Concat(projectSymbols));
            AddSymbolVariant(variants, baseSymbols.Concat(directiveSymbols));
            AddSymbolVariant(
                variants,
                baseSymbols.Concat(windowsSymbols).Concat(directiveSymbols));
            IEnumerable<string> nonWindowsDirectiveSymbols = directiveSymbols.Except(
                windowsSymbols,
                StringComparer.Ordinal);
            AddSymbolVariant(variants, baseSymbols.Concat(nonWindowsDirectiveSymbols));
            AddSymbolVariant(
                variants,
                baseSymbols.Concat(nonWindowsDirectiveSymbols).Concat(["DEBUG", "TRACE"]));
            foreach (string[] positiveSymbols in directivePositiveSets)
            {
                AddSymbolVariant(variants, baseSymbols.Concat(positiveSymbols));
                AddSymbolVariant(
                    variants,
                    baseSymbols.Concat(positiveSymbols).Concat(["DEBUG", "TRACE"]));
                AddSymbolVariant(
                    variants,
                    baseSymbols.Concat(windowsSymbols).Concat(positiveSymbols));
            }

            return variants.Values.ToArray();
        }

        private static bool IsPreprocessorSymbol(string value)
        {
            return value is not ("true" or "false" or "defined");
        }

        private static void AddSymbolVariant(
            IDictionary<string, string[]> variants,
            IEnumerable<string> symbols)
        {
            string[] normalized = symbols
                .Distinct(StringComparer.Ordinal)
                .OrderBy(symbol => symbol, StringComparer.Ordinal)
                .ToArray();
            variants.TryAdd(string.Join(';', normalized), normalized);
        }

        private static HashSet<string> GetProjectDefinedSymbols(
            string repositoryRoot,
            string compilationId)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            string projectPath = Path.Combine(
                repositoryRoot,
                compilationId.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(projectPath))
            {
                return result;
            }

            try
            {
                XDocument project = XDocument.Load(projectPath);
                foreach (XElement constants in project.Descendants()
                             .Where(element => element.Name.LocalName == "DefineConstants"))
                {
                    foreach (string symbol in constants.Value.Split(
                                 [';', ',', ' '],
                                 StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (symbol.All(character =>
                                char.IsLetterOrDigit(character) || character == '_')
                            && symbol != "DefineConstants")
                        {
                            result.Add(symbol);
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is IOException
                                       or UnauthorizedAccessException
                                       or System.Xml.XmlException)
            {
            }

            return result;
        }

        private static HashSet<string> GetProductionCompilationIds(
            string repositoryRoot,
            IReadOnlyList<SourceDocument> documents)
        {
            if (repositoryRoot == "synthetic")
            {
                return documents
                    .Where(document => document.RelativePath.StartsWith(
                        "src/",
                        StringComparison.Ordinal))
                    .Select(document => document.CompilationId)
                    .ToHashSet(StringComparer.Ordinal);
            }

            string[] projects = documents
                .Select(document => document.CompilationId)
                .Where(compilationId =>
                    compilationId.StartsWith("src/", StringComparison.Ordinal)
                    && compilationId.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var references = projects.ToDictionary(
                project => project,
                project => GetProjectReferences(repositoryRoot, project),
                StringComparer.Ordinal);
            var result = projects
                .Where(project => string.Equals(
                    project,
                    "src/Beutl.Engine/Beutl.Engine.csproj",
                    StringComparison.Ordinal))
                .ToHashSet(StringComparer.Ordinal);
            if (result.Count == 0)
            {
                throw new InvalidOperationException(
                    "The renderer census could not locate "
                    + "'src/Beutl.Engine/Beutl.Engine.csproj'.");
            }

            bool changed;
            do
            {
                changed = false;
                foreach (string project in projects)
                {
                    if (!result.Contains(project)
                        && references[project].Any(result.Contains))
                    {
                        result.Add(project);
                        changed = true;
                    }
                }
            } while (changed);

            return result;
        }

        private static string[] GetProjectReferences(
            string repositoryRoot,
            string compilationId)
        {
            string projectPath = Path.Combine(
                repositoryRoot,
                compilationId.Replace('/', Path.DirectorySeparatorChar));
            try
            {
                XDocument project = XDocument.Load(projectPath);
                return project.Descendants()
                    .Where(element => element.Name.LocalName == "ProjectReference")
                    .Select(element => (string?)element.Attribute("Include"))
                    .OfType<string>()
                    .Select(reference => NormalizePath(Path.GetRelativePath(
                        repositoryRoot,
                        Path.GetFullPath(
                            reference.Replace('\\', Path.DirectorySeparatorChar),
                            Path.GetDirectoryName(projectPath)!))))
                    .ToArray();
            }
            catch (Exception ex) when (ex is IOException
                                       or UnauthorizedAccessException
                                       or ArgumentException
                                       or NotSupportedException
                                       or System.Xml.XmlException)
            {
                return [];
            }
        }

        private static string GetQualifiedTypeName(TypeDeclarationSyntax type)
        {
            IEnumerable<string> namespaces = type.Ancestors()
                .OfType<BaseNamespaceDeclarationSyntax>()
                .Reverse()
                .Select(item => GetWrittenTypeIdentity(item.Name));
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
                    foreach (DeclaredType candidate in GetMemberDeclaredTypes(
                                 current.Document).Where(candidate =>
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

        private bool CanReceiveType(
            TypeSyntax? receiverType,
            SourceDocument document,
            string namespaceName,
            string typeName,
            string qualifiedTypeName)
        {
            if (receiverType is IdentifierNameSyntax typeParameter)
            {
                TypeSyntax[] constraints = GetTypeParameterConstraints(typeParameter).ToArray();
                if (constraints.Length > 0)
                {
                    return constraints.Any(constraint => CanReceiveType(
                        constraint,
                        document,
                        namespaceName,
                        typeName,
                        qualifiedTypeName));
                }
            }

            if (CouldReferToType(
                    receiverType,
                    document,
                    namespaceName,
                    typeName,
                    qualifiedTypeName))
            {
                return true;
            }

            foreach (DeclaredType target in _memberDeclaredTypes.Value.Where(item =>
                         !item.IsFileLocal
                         && item.Document.RelativePath.StartsWith("src/", StringComparison.Ordinal)
                         && _productionCompilationIds.Contains(item.Document.CompilationId)
                         && item.Document.VariantId == document.VariantId
                         && item.QualifiedName == qualifiedTypeName))
            {
                DeclaredType[] hierarchy = EnumerateTypeHierarchy(target).ToArray();
                if (hierarchy.Any(candidate =>
                        CanReceiveReferencedBase(receiverType, document, candidate)))
                {
                    return true;
                }

                foreach (DeclaredType baseType in hierarchy.Skip(1))
                {
                    if (CouldReferToType(
                            receiverType,
                            document,
                            GetNamespaceName(baseType.Syntax),
                            GetTypeIdentityName(baseType.Syntax),
                            baseType.QualifiedName))
                    {
                        return true;
                    }
                }
            }

            return false;
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

            if (type is IdentifierNameSyntax typeParameter)
            {
                return GetTypeParameterConstraints(typeParameter)
                    .Any(constraint => CouldReferToType(
                        constraint,
                        document,
                        namespaceName,
                        typeName,
                        qualifiedTypeName));
            }

            string writtenType = GetWrittenTypeIdentity(type);
            if (writtenType == qualifiedTypeName)
            {
                return true;
            }

            AliasQualifiedNameSyntax? aliasQualified = type.DescendantNodesAndSelf()
                .OfType<AliasQualifiedNameSyntax>()
                .FirstOrDefault();
            if (aliasQualified is not null
                && aliasQualified.Alias.Identifier.ValueText != "global")
            {
                string externAliasName = aliasQualified.Alias.Identifier.ValueText;
                string prefix = externAliasName + ".";
                return writtenType.StartsWith(prefix, StringComparison.Ordinal)
                       && writtenType[prefix.Length..] == qualifiedTypeName
                       && ExternAliasTargetsEngine(externAliasName, document);
            }

            if (IsGloballyQualified(type))
            {
                return false;
            }

            UsingDirectiveSyntax[] usings = type.Ancestors()
                .OfType<BaseNamespaceDeclarationSyntax>()
                .SelectMany(item => item.Usings)
                .Concat(document.Root.Usings.Where(item =>
                    !item.GlobalKeyword.IsKind(SyntaxKind.GlobalKeyword)))
                .ToArray();
            UsingDirectiveSyntax[]? globalUsings = null;
            if (!_memberGlobalUsings.Value.TryGetValue(
                    (document.CompilationId, document.VariantId),
                    out globalUsings))
            {
                _globalUsings.TryGetValue(document.CompilationId, out globalUsings);
            }

            if (globalUsings is not null)
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
                    : GetWrittenTypeIdentity(alias.Name);
                string suffix = nameSeparator < 0 ? string.Empty : writtenType[nameSeparator..];
                return AliasResolvesTo(
                    alias,
                    aliasTarget,
                    suffix,
                    qualifiedTypeName,
                    document);
            }

            string declaredNamespace = GetNamespaceName(type);
            if (writtenType.Contains('.', StringComparison.Ordinal))
            {
                for (string scope = declaredNamespace; ;)
                {
                    string candidate = string.IsNullOrEmpty(scope)
                        ? writtenType
                        : scope + "." + writtenType;
                    DeclaredType? visible = GetMemberDeclaredTypes(document).FirstOrDefault(item =>
                        IsVisibleIn(item, document)
                        && item.QualifiedName == candidate);
                    if (visible is not null || candidate == qualifiedTypeName)
                    {
                        return candidate == qualifiedTypeName;
                    }

                    int separator = scope.LastIndexOf('.');
                    if (separator < 0)
                    {
                        break;
                    }

                    scope = scope[..separator];
                }

                return usings
                    .Where(item => item.Alias is null && item.Name is not null)
                    .Any(item => ImportResolvesTo(
                        item,
                        writtenType,
                        qualifiedTypeName,
                        document));
            }

            if (writtenType != typeName)
            {
                return false;
            }

            for (string scope = declaredNamespace; ;)
            {
                string declaredType = string.IsNullOrEmpty(scope)
                    ? typeName
                    : scope + "." + typeName;
                if (GetMemberDeclaredTypes(document).Any(item =>
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
                       && ImportResolvesTo(
                           item,
                           string.Empty,
                           namespaceName,
                           document));
        }

        private bool ImportResolvesTo(
            UsingDirectiveSyntax directive,
            string suffix,
            string qualifiedTarget,
            SourceDocument document)
        {
            foreach (string candidate in GetImportCandidates(
                         directive,
                         GetWrittenName(directive.Name!)))
            {
                string resolved = string.IsNullOrEmpty(suffix)
                    ? candidate
                    : candidate + "." + suffix;
                if (NamespaceExistsInCompilation(candidate, document)
                    || resolved == qualifiedTarget)
                {
                    return resolved == qualifiedTarget;
                }
            }

            return false;
        }

        private bool ExternAliasTargetsEngine(
            string aliasName,
            SourceDocument document)
        {
            bool declared = document.Root.DescendantNodes()
                .OfType<ExternAliasDirectiveSyntax>()
                .Any(directive => directive.Identifier.ValueText == aliasName);
            if (!declared)
            {
                return false;
            }

            if (RepositoryRoot == "synthetic")
            {
                return aliasName == "Engine";
            }

            string projectPath = Path.Combine(
                RepositoryRoot,
                document.CompilationId.Replace('/', Path.DirectorySeparatorChar));
            try
            {
                XDocument project = XDocument.Load(projectPath);
                foreach (XElement reference in project.Descendants()
                             .Where(element => element.Name.LocalName == "ProjectReference"))
                {
                    string? include = (string?)reference.Attribute("Include");
                    string? aliases = reference.Elements()
                        .FirstOrDefault(element => element.Name.LocalName == "Aliases")?
                        .Value;
                    if (include is null
                        || aliases is null
                        || !aliases.Split(
                                [';', ',', ' '],
                                StringSplitOptions.RemoveEmptyEntries)
                            .Contains(aliasName, StringComparer.Ordinal))
                    {
                        continue;
                    }

                    string referencedProject = NormalizePath(Path.GetRelativePath(
                        RepositoryRoot,
                        Path.GetFullPath(
                            include.Replace('\\', Path.DirectorySeparatorChar),
                            Path.GetDirectoryName(projectPath)!)));
                    return referencedProject == "src/Beutl.Engine/Beutl.Engine.csproj";
                }
            }
            catch (Exception ex) when (ex is IOException
                                       or UnauthorizedAccessException
                                       or ArgumentException
                                       or NotSupportedException
                                       or System.Xml.XmlException)
            {
            }

            return false;
        }

        private bool NamespaceExistsInCompilation(
            string namespaceName,
            SourceDocument document)
        {
            string prefix = namespaceName + ".";
            return GetMemberDeclaredTypes(document).Any(item =>
                item.QualifiedName.StartsWith(prefix, StringComparison.Ordinal));
        }

        private bool CanReceiveReferencedBase(
            TypeSyntax? receiverType,
            SourceDocument receiverDocument,
            DeclaredType target)
        {
            if (receiverType is null)
            {
                return false;
            }

            HashSet<string> receiverIdentities = GetTypeReferenceCandidates(
                receiverType,
                receiverDocument);
            if (receiverIdentities.Contains("object")
                || receiverIdentities.Contains("System.Object"))
            {
                return true;
            }

            return target.Syntax.BaseList?.Types.Any(baseType =>
                GetTypeReferenceCandidates(baseType.Type, target.Document)
                    .Overlaps(receiverIdentities)) == true;
        }

        private static IEnumerable<TypeSyntax> GetTypeParameterConstraints(
            IdentifierNameSyntax typeParameter)
        {
            return typeParameter.Ancestors()
                .OfType<MethodDeclarationSyntax>()
                .SelectMany(method => method.ConstraintClauses)
                .Concat(typeParameter.Ancestors()
                    .FirstOrDefault(node => node.IsKind(SyntaxKind.ExtensionBlockDeclaration))?
                    .ChildNodes()
                    .OfType<TypeParameterConstraintClauseSyntax>() ?? [])
                .Where(item =>
                    item.Name.Identifier.ValueText == typeParameter.Identifier.ValueText)
                .SelectMany(item => item.Constraints.OfType<TypeConstraintSyntax>())
                .Select(constraint => constraint.Type);
        }

        private bool AliasResolvesTo(
            UsingDirectiveSyntax alias,
            string aliasTarget,
            string suffix,
            string qualifiedTarget,
            SourceDocument document)
        {
            foreach (string candidate in GetImportCandidates(alias, aliasTarget))
            {
                string resolved = candidate + suffix;
                if (GetMemberDeclaredTypes(document).Any(item =>
                        item.QualifiedName == resolved)
                    || resolved == qualifiedTarget)
                {
                    return resolved == qualifiedTarget;
                }
            }

            return false;
        }

        private HashSet<string> GetTypeReferenceCandidates(
            TypeSyntax type,
            SourceDocument document)
        {
            if (type is NullableTypeSyntax nullable)
            {
                type = nullable.ElementType;
            }

            string written = GetWrittenTypeIdentity(type);
            var result = new HashSet<string>(StringComparer.Ordinal) { written };
            if (IsGloballyQualified(type))
            {
                return result;
            }

            string declaredNamespace = GetNamespaceName(type);
            for (string scope = declaredNamespace; ;)
            {
                result.Add(string.IsNullOrEmpty(scope) ? written : scope + "." + written);
                int separator = scope.LastIndexOf('.');
                if (separator < 0)
                {
                    break;
                }

                scope = scope[..separator];
            }

            UsingDirectiveSyntax[] usings = type.Ancestors()
                .OfType<BaseNamespaceDeclarationSyntax>()
                .SelectMany(item => item.Usings)
                .Concat(document.Root.Usings)
                .Where(item => item.Alias is null && item.Name is not null)
                .ToArray();
            foreach (UsingDirectiveSyntax import in usings)
            {
                foreach (string importedNamespace in GetImportCandidates(
                             import,
                             GetWrittenName(import.Name!)))
                {
                    result.Add(importedNamespace + "." + written);
                }
            }

            return result;
        }

        private IEnumerable<DeclaredType> GetMemberDeclaredTypes(SourceDocument document)
        {
            IEnumerable<DeclaredType> candidates = document.VariantId == "default"
                ? _declaredTypes
                : _memberDeclaredTypes.Value;
            return candidates.Where(item =>
                item.Document.CompilationId == document.CompilationId
                && (document.VariantId == "default"
                    || item.Document.VariantId == document.VariantId)
                && item.Document.RelativePath.StartsWith("src/", StringComparison.Ordinal)
                && _productionCompilationIds.Contains(item.Document.CompilationId));
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

        private static bool IsGloballyQualified(TypeSyntax type)
        {
            return type.DescendantNodesAndSelf()
                .OfType<AliasQualifiedNameSyntax>()
                .Any(alias => alias.Alias.Identifier.ValueText == "global");
        }

        private static string GetWrittenName(NameSyntax name)
        {
            return GetWrittenTypeIdentity(name);
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
                .Select(item => GetWrittenTypeIdentity(item.Name)));
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

        private static bool IsExternallyAccessible(
            MemberDeclarationSyntax member,
            TypeDeclarationSyntax containingType)
        {
            SyntaxTokenList modifiers = member switch
            {
                BaseMethodDeclarationSyntax method => method.Modifiers,
                BasePropertyDeclarationSyntax property => property.Modifiers,
                BaseFieldDeclarationSyntax field => field.Modifiers,
                _ => default,
            };
            return modifiers.Any(SyntaxKind.PublicKeyword)
                   || modifiers.Any(SyntaxKind.ProtectedKeyword)
                   && !modifiers.Any(SyntaxKind.PrivateKeyword)
                   || containingType is InterfaceDeclarationSyntax
                   && !modifiers.Any(SyntaxKind.PrivateKeyword);
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
        public string VariantId { get; init; } = "default";

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
