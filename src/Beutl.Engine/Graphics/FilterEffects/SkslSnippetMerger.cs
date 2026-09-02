using System.Collections.ObjectModel;
using System.Text;

namespace Beutl.Graphics.Effects;

/// <summary>
/// Composes a leading WholeSource shader and validated CurrentPixel snippets into whole-source SkSL while preserving
/// authored order. CurrentPixel declarations and the WholeSource entry point are alpha-renamed from lexer token
/// offsets, so comments and member/swizzle identifiers cannot be corrupted by textual replacement. Runs split before
/// the first stage that would overflow the selected backend budget. A stage that cannot fit by itself is returned as a
/// one-stage standalone fallback instead of disappearing.
/// </summary>
internal static class SkslSnippetMerger
{
    public const string SourceChildName = "src";

    private const string GeneratedPixelName = "__beutl_pixel";
    private const string HeadPrefix = "__beutl_head_";
    private const string HeadEntryPointName = HeadPrefix + "main";
    private const string StagePrefix = "__beutl_s";
    private const string SourceHeader = "uniform shader src;\n";
    private const string MainHeader =
        "half4 main(float2 coord) {\n"
        + "    half4 " + GeneratedPixelName + " = src.eval(coord);\n";
    private const string HeadMainHeader =
        "half4 main(float2 coord) {\n"
        + "    half4 " + GeneratedPixelName + " = " + HeadEntryPointName + "(coord);\n";
    private const string MainFooter = "    return " + GeneratedPixelName + ";\n}\n";
    private static readonly HashSet<string> s_headEntryPoint = new(StringComparer.Ordinal) { "main" };
    private static readonly int s_currentPixelFixedSourceByteCount =
        Encoding.UTF8.GetByteCount(SourceHeader)
        + Encoding.UTF8.GetByteCount(MainHeader)
        + Encoding.UTF8.GetByteCount(MainFooter);
    private static readonly int s_currentPixelFixedProgramTokenCount =
        SkslLexer.Tokenize(SourceHeader).Count
        + SkslLexer.Tokenize(MainHeader).Count
        + SkslLexer.Tokenize(MainFooter).Count;
    private static readonly int s_headFixedSourceByteCount =
        Encoding.UTF8.GetByteCount(HeadMainHeader)
        + Encoding.UTF8.GetByteCount(MainFooter);
    private static readonly int s_headFixedProgramTokenCount =
        SkslLexer.Tokenize(HeadMainHeader).Count
        + SkslLexer.Tokenize(MainFooter).Count;

    public static SkslMergedProgram Merge(IReadOnlyList<SkslSnippetStage> stages)
    {
        PreparedStage[] prepared = ValidateAndPrepare(stages);
        return CreateProgram(
            prepared,
            SkslBackendBudget.Unlimited,
            CalculateMetrics(prepared));
    }

    public static IReadOnlyList<SkslMergedProgram> MergeAndSplit(
        IReadOnlyList<SkslSnippetStage> stages,
        SkslBackendBudget budget)
    {
        ArgumentNullException.ThrowIfNull(budget);
        PreparedStage[] prepared = ValidateAndPrepare(stages);

        var result = new List<SkslMergedProgram>();
        var current = new List<PreparedStage>();
        ProgramMetrics currentMetrics = default;
        foreach (PreparedStage stage in prepared)
        {
            if (current.Count == 0)
                currentMetrics = ProgramMetrics.CreateEmpty(stage.IsWholeSourceHead);
            ProgramMetrics candidateMetrics = currentMetrics.Add(stage);
            if (FitsBudget(candidateMetrics, budget))
            {
                current.Add(stage);
                currentMetrics = candidateMetrics;
                continue;
            }

            if (current.Count != 0)
            {
                result.Add(CreateProgram(current, budget, currentMetrics));
                current.Clear();
                currentMetrics = ProgramMetrics.CreateEmpty(stage.IsWholeSourceHead);
                candidateMetrics = currentMetrics.Add(stage);
            }

            if (!FitsBudget(candidateMetrics, budget))
            {
                result.Add(CreateProgram([stage], budget, candidateMetrics));
            }
            else
            {
                current.Add(stage);
                currentMetrics = candidateMetrics;
            }
        }

        if (current.Count != 0)
            result.Add(CreateProgram(current, budget, currentMetrics));

        return new ReadOnlyCollection<SkslMergedProgram>(result);
    }

    private static PreparedStage[] ValidateAndPrepare(IReadOnlyList<SkslSnippetStage> stages)
    {
        ArgumentNullException.ThrowIfNull(stages);
        if (stages.Count == 0)
            throw new ArgumentException("At least one shader stage is required.", nameof(stages));

        var result = new PreparedStage[stages.Count];
        for (int index = 0; index < stages.Count; index++)
        {
            SkslSnippetStage stage = stages[index]
                ?? throw new ArgumentException("A shader stage cannot be null.", nameof(stages));
            if (index != 0 && stage.Description.Kind == ShaderDescriptionKind.WholeSource)
            {
                throw new ArgumentException(
                    "A WholeSource shader can participate only as the first stage of a snippet run.",
                    nameof(stages));
            }
            result[index] = Prepare(index, stage);
        }
        return result;
    }

    private static PreparedStage Prepare(int index, SkslSnippetStage stage)
    {
        bool isWholeSourceHead = stage.Description.Kind == ShaderDescriptionKind.WholeSource;
        string prefix = isWholeSourceHead ? HeadPrefix : GetPrefix(index);
        IReadOnlySet<string> renamedNames = isWholeSourceHead
            ? s_headEntryPoint
            : stage.Description.Source.TopLevelSymbols;
        RenameResult renamed = Rename(stage.Description.Source, prefix, renamedNames);
        bool appendNewline = renamed.Source.Length == 0 || renamed.Source[^1] != '\n';
        string invocation = isWholeSourceHead ? string.Empty : CreateInvocation(prefix);
        var bindings = new List<SkslMergedBindingLayout>(
            stage.Description.Uniforms.Count + stage.Description.Resources.Count);
        AddBindings(index, stage, isWholeSourceHead ? string.Empty : prefix, bindings);
        int sourceBytes = Encoding.UTF8.GetByteCount(renamed.Source);
        if (appendNewline)
            sourceBytes = SaturatingAdd(sourceBytes, 1);
        int programTokens = renamed.TokenCount;
        if (invocation.Length != 0)
        {
            sourceBytes = SaturatingAdd(sourceBytes, Encoding.UTF8.GetByteCount(invocation));
            programTokens = SaturatingAdd(
                programTokens,
                SkslLexer.Tokenize(invocation).Count);
        }
        return new PreparedStage(
            index,
            stage,
            isWholeSourceHead,
            prefix,
            renamed.Source,
            appendNewline,
            invocation,
            bindings.ToArray(),
            GetUniformVectorCount(stage.Description.Source),
            stage.Description.Resources.Count,
            sourceBytes,
            programTokens);
    }

    private static SkslMergedProgram CreateProgram(
        IReadOnlyList<PreparedStage> stages,
        SkslBackendBudget budget,
        ProgramMetrics metrics)
    {
        var source = new StringBuilder();
        bool hasWholeSourceHead = stages[0].IsWholeSourceHead;
        if (!hasWholeSourceHead)
            source.Append(SourceHeader);
        var stageLayouts = new List<SkslMergedStageLayout>(stages.Count);
        var bindingLayouts = new List<SkslMergedBindingLayout>();

        foreach (PreparedStage prepared in stages)
        {
            source.Append(prepared.Source);
            if (prepared.AppendNewline)
                source.Append('\n');

            stageLayouts.Add(new SkslMergedStageLayout(
                prepared.Index,
                prepared.Prefix,
                prepared.Stage.CoverageBehavior));
            bindingLayouts.AddRange(prepared.Bindings);
        }

        source.Append(hasWholeSourceHead ? HeadMainHeader : MainHeader);
        foreach (PreparedStage prepared in stages)
            source.Append(prepared.Invocation);
        source.Append(MainFooter);

        string mergedSource = source.ToString();
        IReadOnlyList<SkslBackendLimit> overflow = GetOverflowReasons(
            metrics.StageCount,
            metrics.UniformVectorCount,
            metrics.SamplerCount,
            metrics.ChildCount,
            metrics.SourceByteCount,
            metrics.ProgramTokenCount,
            budget);

        return new SkslMergedProgram(
            mergedSource,
            stageLayouts,
            bindingLayouts,
            budget,
            metrics.UniformVectorCount,
            metrics.SamplerCount,
            metrics.ChildCount,
            metrics.SourceByteCount,
            metrics.ProgramTokenCount,
            overflow);
    }

    internal static bool IsRendererGeneratedName(string name)
    {
        if (name is GeneratedPixelName or HeadEntryPointName)
            return true;
        if (!name.StartsWith(StagePrefix, StringComparison.Ordinal))
            return false;

        ReadOnlySpan<char> suffix = name.AsSpan(StagePrefix.Length);
        int separator = suffix.IndexOf('_');
        return separator > 0
               && (separator == 1 || suffix[0] != '0')
               && separator + 1 < suffix.Length
               && int.TryParse(suffix[..separator], out int stageIndex)
               && stageIndex >= 0;
    }

    private static string GetPrefix(int stageIndex) => $"{StagePrefix}{stageIndex}_";

    private static string CreateInvocation(string prefix)
        => $"    {GeneratedPixelName} = {prefix}apply({GeneratedPixelName});\n";

    private static RenameResult Rename(
        SkslSource source,
        string prefix,
        IReadOnlySet<string> names)
    {
        List<SkslToken> tokens = SkslLexer.Tokenize(source.Text);
        var result = new StringBuilder(source.Text.Length + (names.Count * prefix.Length));
        int copiedThrough = 0;
        for (int index = 0; index < tokens.Count; index++)
        {
            SkslToken token = tokens[index];
            if (!token.IsIdentifier
                || !names.Contains(token.Text)
                || index > 0 && tokens[index - 1].Text == ".")
            {
                continue;
            }

            result.Append(source.Text, copiedThrough, token.Start - copiedThrough);
            result.Append(prefix).Append(token.Text);
            copiedThrough = token.Start + token.Length;
        }

        result.Append(source.Text, copiedThrough, source.Text.Length - copiedThrough);
        return new RenameResult(result.ToString(), tokens.Count);
    }

    private static ProgramMetrics CalculateMetrics(IReadOnlyList<PreparedStage> stages)
    {
        ProgramMetrics result = ProgramMetrics.CreateEmpty(stages[0].IsWholeSourceHead);
        foreach (PreparedStage stage in stages)
            result = result.Add(stage);
        return result;
    }

    private static void AddBindings(
        int stageIndex,
        SkslSnippetStage stage,
        string prefix,
        List<SkslMergedBindingLayout> result)
    {
        ShaderDescription description = stage.Description;
        for (int bindingIndex = 0; bindingIndex < description.Uniforms.Count; bindingIndex++)
        {
            ShaderUniformBinding binding = description.Uniforms[bindingIndex];
            SkslUniformDeclaration declaration = description.Source.Uniforms[binding.Name];
            result.Add(new SkslMergedBindingLayout(
                stageIndex,
                bindingIndex,
                SkslBindingKind.Uniform,
                binding.Name,
                prefix + binding.Name,
                declaration.Type,
                declaration.ArrayExtent,
                null));
        }

        for (int bindingIndex = 0; bindingIndex < description.Resources.Count; bindingIndex++)
        {
            ShaderResourceBinding binding = description.Resources[bindingIndex];
            SkslUniformDeclaration declaration = description.Source.Uniforms[binding.Name];
            result.Add(new SkslMergedBindingLayout(
                stageIndex,
                bindingIndex,
                SkslBindingKind.Resource,
                binding.Name,
                prefix + binding.Name,
                declaration.Type,
                declaration.ArrayExtent,
                binding.CoordinateSpace));
        }
    }

    private static int GetUniformVectorCount(SkslSource source)
    {
        int result = 0;
        foreach (SkslUniformDeclaration declaration in source.Uniforms.Values)
        {
            if (declaration.IsShader)
                continue;

            int vectors = GetTypeVectorCount(declaration.Type);
            if (declaration.ArrayExtent is int extent)
                vectors = SaturatingMultiply(vectors, extent);
            result = SaturatingAdd(result, vectors);
        }
        return result;
    }

    private static int GetTypeVectorCount(string type)
    {
        if (type is "mat2" or "mat3" or "mat4")
            return type[^1] - '0';

        int separator = type.IndexOf('x', StringComparison.Ordinal);
        if (separator > 0
            && separator + 1 < type.Length
            && type.AsSpan(separator + 1).Length == 1
            && char.IsAsciiDigit(type[separator + 1]))
        {
            return type[separator + 1] - '0';
        }

        return 1;
    }

    private static IReadOnlyList<SkslBackendLimit> GetOverflowReasons(
        int stages,
        int uniforms,
        int samplers,
        int children,
        int sourceBytes,
        int programTokens,
        SkslBackendBudget budget)
    {
        var result = new List<SkslBackendLimit>(6);
        if (stages > budget.MaxStages)
            result.Add(SkslBackendLimit.StageCount);
        if (uniforms > budget.MaxUniformVectors)
            result.Add(SkslBackendLimit.UniformVectors);
        if (samplers > budget.MaxSamplers)
            result.Add(SkslBackendLimit.Samplers);
        if (children > budget.MaxChildren)
            result.Add(SkslBackendLimit.Children);
        if (sourceBytes > budget.MaxSourceBytes)
            result.Add(SkslBackendLimit.SourceBytes);
        if (programTokens > budget.MaxProgramTokens)
            result.Add(SkslBackendLimit.ProgramTokens);
        return result;
    }

    private static bool FitsBudget(ProgramMetrics metrics, SkslBackendBudget budget)
        => metrics.StageCount <= budget.MaxStages
           && metrics.UniformVectorCount <= budget.MaxUniformVectors
           && metrics.SamplerCount <= budget.MaxSamplers
           && metrics.ChildCount <= budget.MaxChildren
           && metrics.SourceByteCount <= budget.MaxSourceBytes
           && metrics.ProgramTokenCount <= budget.MaxProgramTokens;

    private static int SaturatingAdd(int left, int right)
    {
        long result = (long)left + right;
        return result >= int.MaxValue ? int.MaxValue : (int)result;
    }

    private static int SaturatingMultiply(int left, int right)
    {
        long result = (long)left * right;
        return result >= int.MaxValue ? int.MaxValue : (int)result;
    }

    private sealed record PreparedStage(
        int Index,
        SkslSnippetStage Stage,
        bool IsWholeSourceHead,
        string Prefix,
        string Source,
        bool AppendNewline,
        string Invocation,
        SkslMergedBindingLayout[] Bindings,
        int UniformVectorCount,
        int ResourceCount,
        int SourceByteCount,
        int ProgramTokenCount);

    private readonly record struct RenameResult(string Source, int TokenCount);

    private readonly record struct ProgramMetrics(
        int StageCount,
        int UniformVectorCount,
        int SamplerCount,
        int ChildCount,
        int SourceByteCount,
        int ProgramTokenCount)
    {
        public static ProgramMetrics CreateEmpty(bool hasWholeSourceHead)
            => new(
                0,
                0,
                1,
                1,
                hasWholeSourceHead
                    ? s_headFixedSourceByteCount
                    : s_currentPixelFixedSourceByteCount,
                hasWholeSourceHead
                    ? s_headFixedProgramTokenCount
                    : s_currentPixelFixedProgramTokenCount);

        public ProgramMetrics Add(PreparedStage stage)
            => new(
                SaturatingAdd(StageCount, 1),
                SaturatingAdd(UniformVectorCount, stage.UniformVectorCount),
                SaturatingAdd(SamplerCount, stage.ResourceCount),
                SaturatingAdd(ChildCount, stage.ResourceCount),
                SaturatingAdd(SourceByteCount, stage.SourceByteCount),
                SaturatingAdd(ProgramTokenCount, stage.ProgramTokenCount));
    }
}
