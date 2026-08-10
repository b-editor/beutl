using System.Collections.ObjectModel;
using System.Text;

namespace Beutl.Graphics.Effects;

internal enum SkslCoverageBehavior
{
    RequiresResolvedCoverage,
    PremultipliedCoverageHomogeneous,
}

internal enum SkslBindingKind
{
    Uniform,
    Resource,
}

internal enum SkslBackendLimit
{
    StageCount,
    UniformVectors,
    Samplers,
    Children,
    SourceBytes,
    ProgramTokens,
}

internal sealed class SkslSnippetStage
{
    public SkslSnippetStage(
        ShaderDescription description,
        SkslCoverageBehavior coverageBehavior = SkslCoverageBehavior.RequiresResolvedCoverage)
    {
        ArgumentNullException.ThrowIfNull(description);
        if (description.Kind != ShaderDescriptionKind.CurrentPixel)
        {
            throw new ArgumentException(
                "Only validated CurrentPixel shader descriptions can participate in a merged snippet run.",
                nameof(description));
        }
        if (!Enum.IsDefined(coverageBehavior))
            throw new ArgumentOutOfRangeException(nameof(coverageBehavior));

        Description = description;
        CoverageBehavior = coverageBehavior;
    }

    public ShaderDescription Description { get; }

    public SkslCoverageBehavior CoverageBehavior { get; }
}

internal sealed class SkslBackendBudget : IEquatable<SkslBackendBudget>
{
    private static readonly object s_unlimitedCapability = new();

    public SkslBackendBudget(
        object capabilityClass,
        int maxStages,
        int maxUniformVectors,
        int maxSamplers,
        int maxChildren,
        int maxSourceBytes,
        int maxProgramTokens)
    {
        ArgumentNullException.ThrowIfNull(capabilityClass);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxStages, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(maxUniformVectors);
        ArgumentOutOfRangeException.ThrowIfNegative(maxSamplers);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxChildren, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxSourceBytes, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxProgramTokens, 1);

        CapabilityClass = capabilityClass;
        MaxStages = maxStages;
        MaxUniformVectors = maxUniformVectors;
        MaxSamplers = maxSamplers;
        MaxChildren = maxChildren;
        MaxSourceBytes = maxSourceBytes;
        MaxProgramTokens = maxProgramTokens;
    }

    public static SkslBackendBudget Unlimited { get; } = new(
        s_unlimitedCapability,
        int.MaxValue,
        int.MaxValue,
        int.MaxValue,
        int.MaxValue,
        int.MaxValue,
        int.MaxValue);

    public object CapabilityClass { get; }

    public int MaxStages { get; }

    public int MaxUniformVectors { get; }

    public int MaxSamplers { get; }

    public int MaxChildren { get; }

    public int MaxSourceBytes { get; }

    public int MaxProgramTokens { get; }

    public bool Equals(SkslBackendBudget? other)
        => other is not null
           && Equals(CapabilityClass, other.CapabilityClass)
           && MaxStages == other.MaxStages
           && MaxUniformVectors == other.MaxUniformVectors
           && MaxSamplers == other.MaxSamplers
           && MaxChildren == other.MaxChildren
           && MaxSourceBytes == other.MaxSourceBytes
           && MaxProgramTokens == other.MaxProgramTokens;

    public override bool Equals(object? obj) => obj is SkslBackendBudget other && Equals(other);

    public override int GetHashCode()
        => HashCode.Combine(
            CapabilityClass,
            MaxStages,
            MaxUniformVectors,
            MaxSamplers,
            MaxChildren,
            MaxSourceBytes,
            MaxProgramTokens);
}

internal sealed record SkslMergedBindingLayout(
    int StageIndex,
    int BindingIndex,
    SkslBindingKind Kind,
    string OriginalName,
    string MergedName,
    string Type,
    int? ArrayExtent,
    ShaderResourceCoordinateSpace? CoordinateSpace);

internal sealed record SkslMergedStageLayout(
    int StageIndex,
    string Prefix,
    SkslCoverageBehavior CoverageBehavior);

internal sealed class SkslMergedProgram
{
    internal SkslMergedProgram(
        string source,
        IReadOnlyList<SkslMergedStageLayout> stages,
        IReadOnlyList<SkslMergedBindingLayout> bindings,
        SkslBackendBudget budget,
        int uniformVectorCount,
        int samplerCount,
        int childCount,
        int sourceByteCount,
        int programTokenCount,
        IReadOnlyList<SkslBackendLimit> overflowReasons)
    {
        Source = source;
        Stages = new ReadOnlyCollection<SkslMergedStageLayout>(stages.ToArray());
        Bindings = new ReadOnlyCollection<SkslMergedBindingLayout>(bindings.ToArray());
        Budget = budget;
        UniformVectorCount = uniformVectorCount;
        SamplerCount = samplerCount;
        ChildCount = childCount;
        SourceByteCount = sourceByteCount;
        ProgramTokenCount = programTokenCount;
        OverflowReasons = new ReadOnlyCollection<SkslBackendLimit>(overflowReasons.ToArray());
        Identity = new SkslMergedProgramIdentity(Source, Bindings, Budget);
    }

    public string Source { get; }

    public IReadOnlyList<SkslMergedStageLayout> Stages { get; }

    public IReadOnlyList<SkslMergedBindingLayout> Bindings { get; }

    public SkslBackendBudget Budget { get; }

    public SkslMergedProgramIdentity Identity { get; }

    public int StageCount => Stages.Count;

    public int UniformVectorCount { get; }

    public int SamplerCount { get; }

    public int ChildCount { get; }

    public int SourceByteCount { get; }

    public int ProgramTokenCount { get; }

    public IReadOnlyList<SkslBackendLimit> OverflowReasons { get; }

    public bool RequiresStandaloneExecution => OverflowReasons.Count != 0;

    public bool IsPremultipliedCoverageHomogeneous
        => Stages.All(static stage =>
            stage.CoverageBehavior == SkslCoverageBehavior.PremultipliedCoverageHomogeneous);

    public bool RequiresResolvedCoverage => !IsPremultipliedCoverageHomogeneous;
}

/// <summary>
/// A program-cache bucket identity. The stable hash is only the bucket selector; equality compares the complete
/// generated source, binding signature, capability class, and relevant backend limits.
/// </summary>
internal sealed class SkslMergedProgramIdentity : IEquatable<SkslMergedProgramIdentity>
{
    private readonly SkslMergedBindingLayout[] _bindings;

    internal SkslMergedProgramIdentity(
        string source,
        IReadOnlyList<SkslMergedBindingLayout> bindings,
        SkslBackendBudget budget,
        int? bucketHashOverride = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(budget);

        Source = source;
        _bindings = bindings.ToArray();
        Budget = budget;
        BucketHash = bucketHashOverride ?? ComputeStableBucketHash(source);
    }

    public int BucketHash { get; }

    private string Source { get; }

    private SkslBackendBudget Budget { get; }

    internal static SkslMergedProgramIdentity CreateStandalone(
        string source,
        SkslBackendBudget budget)
        => new(source, [], budget);

    public bool Equals(SkslMergedProgramIdentity? other)
        => other is not null
           && BucketHash == other.BucketHash
           && Source == other.Source
           && Budget.Equals(other.Budget)
           && _bindings.AsSpan().SequenceEqual(other._bindings);

    public override bool Equals(object? obj) => obj is SkslMergedProgramIdentity other && Equals(other);

    public override int GetHashCode() => BucketHash;

    private static int ComputeStableBucketHash(string source)
    {
        const uint offset = 2166136261;
        const uint prime = 16777619;
        uint hash = offset;
        foreach (char value in source)
        {
            hash ^= value;
            hash *= prime;
        }
        return unchecked((int)hash);
    }
}

/// <summary>
/// Composes validated CurrentPixel snippets into whole-source SkSL while preserving authored order. All accepted
/// declarations are alpha-renamed from lexer token offsets, so comments and member/swizzle identifiers cannot be
/// corrupted by textual replacement. Runs split before the first stage that would overflow the selected backend
/// budget. A stage that cannot fit by itself is returned as a one-stage standalone fallback instead of disappearing.
/// </summary>
internal static class SkslSnippetMerger
{
    public const string SourceChildName = "src";

    private const string GeneratedPixelName = "__beutl_pixel";
    private const string SourceHeader = "uniform shader src;\n";
    private const string MainHeader =
        "half4 main(float2 coord) {\n"
        + "    half4 __beutl_pixel = src.eval(coord);\n";
    private const string MainFooter = "    return __beutl_pixel;\n}\n";
    private static readonly int s_fixedSourceByteCount =
        Encoding.UTF8.GetByteCount(SourceHeader)
        + Encoding.UTF8.GetByteCount(MainHeader)
        + Encoding.UTF8.GetByteCount(MainFooter);
    private static readonly int s_fixedProgramTokenCount =
        SkslLexer.Tokenize(SourceHeader).Count
        + SkslLexer.Tokenize(MainHeader).Count
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
        ProgramMetrics currentMetrics = ProgramMetrics.Empty;
        foreach (PreparedStage stage in prepared)
        {
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
                currentMetrics = ProgramMetrics.Empty;
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
            throw new ArgumentException("At least one CurrentPixel stage is required.", nameof(stages));

        var result = new PreparedStage[stages.Count];
        for (int index = 0; index < stages.Count; index++)
        {
            SkslSnippetStage stage = stages[index]
                ?? throw new ArgumentException("A CurrentPixel stage cannot be null.", nameof(stages));
            result[index] = Prepare(index, stage);
        }
        return result;
    }

    private static PreparedStage Prepare(int index, SkslSnippetStage stage)
    {
        string prefix = GetPrefix(index);
        RenameResult renamed = Rename(stage.Description.Source, prefix);
        bool appendNewline = renamed.Source.Length == 0 || renamed.Source[^1] != '\n';
        string invocation = CreateInvocation(prefix);
        var bindings = new List<SkslMergedBindingLayout>(
            stage.Description.Uniforms.Count + stage.Description.Resources.Count);
        AddBindings(index, stage, prefix, bindings);
        int sourceBytes = Encoding.UTF8.GetByteCount(renamed.Source);
        if (appendNewline)
            sourceBytes = SaturatingAdd(sourceBytes, 1);
        sourceBytes = SaturatingAdd(sourceBytes, Encoding.UTF8.GetByteCount(invocation));
        int programTokens = SaturatingAdd(
            renamed.TokenCount,
            SkslLexer.Tokenize(invocation).Count);
        return new PreparedStage(
            index,
            stage,
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

        source.Append(MainHeader);
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

    private static string GetPrefix(int stageIndex) => $"__beutl_s{stageIndex}_";

    private static string CreateInvocation(string prefix)
        => $"    {GeneratedPixelName} = {prefix}apply({GeneratedPixelName});\n";

    private static RenameResult Rename(SkslSource source, string prefix)
    {
        List<SkslToken> tokens = SkslLexer.Tokenize(source.Text);
        IReadOnlySet<string> names = source.TopLevelSymbols;
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
        ProgramMetrics result = ProgramMetrics.Empty;
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
        public static ProgramMetrics Empty { get; } = new(
            0,
            0,
            1,
            1,
            s_fixedSourceByteCount,
            s_fixedProgramTokenCount);

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
