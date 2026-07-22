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

    public static SkslMergedProgram Merge(IReadOnlyList<SkslSnippetStage> stages)
    {
        IndexedStage[] indexed = ValidateAndIndex(stages);
        return CreateProgram(indexed, SkslBackendBudget.Unlimited);
    }

    public static IReadOnlyList<SkslMergedProgram> MergeAndSplit(
        IReadOnlyList<SkslSnippetStage> stages,
        SkslBackendBudget budget)
    {
        IndexedStage[] indexed = ValidateAndIndex(stages);
        ArgumentNullException.ThrowIfNull(budget);

        var result = new List<SkslMergedProgram>();
        var current = new List<IndexedStage>();
        foreach (IndexedStage stage in indexed)
        {
            var candidate = new List<IndexedStage>(current.Count + 1);
            candidate.AddRange(current);
            candidate.Add(stage);
            SkslMergedProgram candidateProgram = CreateProgram(candidate, budget);
            if (!candidateProgram.RequiresStandaloneExecution)
            {
                current.Add(stage);
                continue;
            }

            if (current.Count != 0)
            {
                result.Add(CreateProgram(current, budget));
                current.Clear();
                candidateProgram = CreateProgram([stage], budget);
            }

            if (candidateProgram.RequiresStandaloneExecution)
            {
                result.Add(candidateProgram);
            }
            else
            {
                current.Add(stage);
            }
        }

        if (current.Count != 0)
            result.Add(CreateProgram(current, budget));

        return new ReadOnlyCollection<SkslMergedProgram>(result);
    }

    private static IndexedStage[] ValidateAndIndex(IReadOnlyList<SkslSnippetStage> stages)
    {
        ArgumentNullException.ThrowIfNull(stages);
        if (stages.Count == 0)
            throw new ArgumentException("At least one CurrentPixel stage is required.", nameof(stages));

        var result = new IndexedStage[stages.Count];
        for (int index = 0; index < stages.Count; index++)
        {
            SkslSnippetStage stage = stages[index]
                ?? throw new ArgumentException("A CurrentPixel stage cannot be null.", nameof(stages));
            result[index] = new IndexedStage(index, stage);
        }
        return result;
    }

    private static SkslMergedProgram CreateProgram(
        IReadOnlyList<IndexedStage> stages,
        SkslBackendBudget budget)
    {
        var source = new StringBuilder();
        source.Append("uniform shader ").Append(SourceChildName).Append(";\n");
        var stageLayouts = new List<SkslMergedStageLayout>(stages.Count);
        var bindingLayouts = new List<SkslMergedBindingLayout>();
        int uniformVectors = 0;
        int childCount = 1;
        int samplerCount = 1;

        foreach (IndexedStage indexed in stages)
        {
            string prefix = GetPrefix(indexed.Index);
            source.Append(Rename(indexed.Stage.Description.Source.Text, prefix));
            if (source.Length == 0 || source[^1] != '\n')
                source.Append('\n');

            stageLayouts.Add(new SkslMergedStageLayout(
                indexed.Index,
                prefix,
                indexed.Stage.CoverageBehavior));
            AddBindings(indexed, prefix, bindingLayouts);
            uniformVectors = SaturatingAdd(
                uniformVectors,
                GetUniformVectorCount(indexed.Stage.Description.Source));
            childCount = SaturatingAdd(childCount, indexed.Stage.Description.Resources.Count);
            samplerCount = SaturatingAdd(samplerCount, indexed.Stage.Description.Resources.Count);
        }

        source.Append("half4 main(float2 coord) {\n")
            .Append("    half4 ").Append(GeneratedPixelName).Append(" = ")
            .Append(SourceChildName).Append(".eval(coord);\n");
        foreach (SkslMergedStageLayout stage in stageLayouts)
        {
            source.Append("    ").Append(GeneratedPixelName).Append(" = ")
                .Append(stage.Prefix).Append("apply(").Append(GeneratedPixelName).Append(");\n");
        }
        source.Append("    return ").Append(GeneratedPixelName).Append(";\n}\n");

        string mergedSource = source.ToString();
        int sourceBytes = Encoding.UTF8.GetByteCount(mergedSource);
        int programTokens = SkslLexer.Tokenize(mergedSource).Count;
        IReadOnlyList<SkslBackendLimit> overflow = GetOverflowReasons(
            stages.Count,
            uniformVectors,
            samplerCount,
            childCount,
            sourceBytes,
            programTokens,
            budget);

        return new SkslMergedProgram(
            mergedSource,
            stageLayouts,
            bindingLayouts,
            budget,
            uniformVectors,
            samplerCount,
            childCount,
            sourceBytes,
            programTokens,
            overflow);
    }

    private static string GetPrefix(int stageIndex) => $"__beutl_s{stageIndex}_";

    private static string Rename(string source, string prefix)
    {
        List<SkslToken> tokens = SkslLexer.Tokenize(source);
        HashSet<string> names = CollectTopLevelNames(tokens);
        var result = new StringBuilder(source.Length + (names.Count * prefix.Length));
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

            result.Append(source, copiedThrough, token.Start - copiedThrough);
            result.Append(prefix).Append(token.Text);
            copiedThrough = token.Start + token.Length;
        }

        result.Append(source, copiedThrough, source.Length - copiedThrough);
        return result.ToString();
    }

    private static HashSet<string> CollectTopLevelNames(IReadOnlyList<SkslToken> tokens)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < tokens.Count; index++)
        {
            SkslToken token = tokens[index];
            if (!token.IsIdentifier || token.Depth != 0)
                continue;

            if (token.Text is "uniform" or "const")
            {
                int cursor = index + 1;
                if (cursor < tokens.Count && tokens[cursor].Text is "lowp" or "mediump" or "highp")
                    cursor++;
                cursor++;
                if (cursor < tokens.Count && tokens[cursor].IsIdentifier)
                    result.Add(tokens[cursor].Text);
                continue;
            }

            if (index + 2 < tokens.Count
                && tokens[index + 1] is { IsIdentifier: true, Depth: 0 }
                && tokens[index + 2].Text == "(")
            {
                result.Add(tokens[index + 1].Text);
                index++;
            }
        }
        return result;
    }

    private static void AddBindings(
        IndexedStage indexed,
        string prefix,
        List<SkslMergedBindingLayout> result)
    {
        ShaderDescription description = indexed.Stage.Description;
        for (int bindingIndex = 0; bindingIndex < description.Uniforms.Count; bindingIndex++)
        {
            ShaderUniformBinding binding = description.Uniforms[bindingIndex];
            SkslUniformDeclaration declaration = description.Source.Uniforms[binding.Name];
            result.Add(new SkslMergedBindingLayout(
                indexed.Index,
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
                indexed.Index,
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

    private readonly record struct IndexedStage(int Index, SkslSnippetStage Stage);
}
