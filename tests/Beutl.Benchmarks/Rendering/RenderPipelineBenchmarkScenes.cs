using System.Collections.ObjectModel;

using Beutl.Media;

namespace Beutl.Benchmarks.Rendering;

internal enum RenderPipelineBenchmarkAnimation
{
    None,
    ParameterOnly,
    StructuralToggle,
}

internal enum RenderPipelineBenchmarkBarrier
{
    None,
    WholeSourceShader,
    SpatialEffect,
    TargetDependency,
}

internal readonly record struct RenderPipelineBenchmarkFrameState(
    float AnimatedAmount,
    bool StructuralVariant);

internal sealed record RenderPipelineBenchmarkSceneDefinition
{
    public RenderPipelineBenchmarkSceneDefinition(
        string name,
        int seed,
        int semanticStageCount,
        int topLevelDrawableCount = 1,
        float contentScale = 0.8f,
        RenderPipelineBenchmarkAnimation animation = RenderPipelineBenchmarkAnimation.None,
        RenderPipelineBenchmarkBarrier barrier = RenderPipelineBenchmarkBarrier.None,
        bool hasStaticPrefixCache = false,
        bool hasTargetDependencies = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfNegative(semanticStageCount);
        ArgumentOutOfRangeException.ThrowIfLessThan(topLevelDrawableCount, 1);
        if (!float.IsFinite(contentScale) || contentScale is <= 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(contentScale), contentScale, "Content scale must be finite and in the range (0, 1].");
        }

        Name = name;
        Seed = seed;
        SemanticStageCount = semanticStageCount;
        TopLevelDrawableCount = topLevelDrawableCount;
        ContentScale = contentScale;
        Animation = animation;
        Barrier = barrier;
        HasStaticPrefixCache = hasStaticPrefixCache;
        HasTargetDependencies = hasTargetDependencies;
    }

    public string Name { get; }

    public int Seed { get; }

    public int SemanticStageCount { get; }

    public int TopLevelDrawableCount { get; }

    public float ContentScale { get; }

    public RenderPipelineBenchmarkAnimation Animation { get; }

    public RenderPipelineBenchmarkBarrier Barrier { get; }

    public bool HasStaticPrefixCache { get; }

    public bool HasTargetDependencies { get; }

    public RenderPipelineBenchmarkFrameState GetFrameState(int frameIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(frameIndex);

        return Animation switch
        {
            RenderPipelineBenchmarkAnimation.ParameterOnly => new(
                AnimatedAmount: 0.75f + ((frameIndex % 60) / 59f * 0.5f),
                StructuralVariant: false),
            RenderPipelineBenchmarkAnimation.StructuralToggle => new(
                AnimatedAmount: 1f,
                StructuralVariant: ((frameIndex / 8) & 1) != 0),
            _ => new(AnimatedAmount: 1f, StructuralVariant: false),
        };
    }
}

/// <summary>
/// Stable workload metadata and source pixels shared by the baseline and feature render-pipeline benchmarks.
/// Scene construction belongs in the benchmark harness so both worktrees consume these exact definitions.
/// </summary>
internal static class RenderPipelineBenchmarkScenes
{
    public const int SourceSeed = 20_040_719;

    public static readonly PixelSize ReferenceSize = new(384, 216);

    public static readonly PixelSize Hd1080 = new(1920, 1080);

    private static readonly RenderPipelineBenchmarkSceneDefinition[] s_all =
    [
        new("NoEffectControl", SourceSeed + 0, semanticStageCount: 0),
        new("SingleShader", SourceSeed + 1, semanticStageCount: 1),
        new("ShaderOpacityShader", SourceSeed + 2, semanticStageCount: 3),
        new(
            "ShaderOpacityShaderBarrier",
            SourceSeed + 3,
            semanticStageCount: 4,
            barrier: RenderPipelineBenchmarkBarrier.WholeSourceShader),
        new("LongInvariantChain", SourceSeed + 4, semanticStageCount: 10),
        new(
            "ParameterOnlyAnimation",
            SourceSeed + 5,
            semanticStageCount: 3,
            animation: RenderPipelineBenchmarkAnimation.ParameterOnly),
        new(
            "StructuralToggle",
            SourceSeed + 6,
            semanticStageCount: 3,
            animation: RenderPipelineBenchmarkAnimation.StructuralToggle),
        new(
            "StaticPrefixAnimatedTail",
            SourceSeed + 7,
            semanticStageCount: 6,
            animation: RenderPipelineBenchmarkAnimation.ParameterOnly,
            hasStaticPrefixCache: true),
        new(
            "MixedSpatialColor",
            SourceSeed + 8,
            semanticStageCount: 5,
            barrier: RenderPipelineBenchmarkBarrier.SpatialEffect),
        new("SmallObjectFixedOverhead", SourceSeed + 9, semanticStageCount: 3, contentScale: 0.1f),
        new(
            "MultipleDrawablesTargetDependencies",
            SourceSeed + 10,
            semanticStageCount: 4,
            topLevelDrawableCount: 4,
            barrier: RenderPipelineBenchmarkBarrier.TargetDependency,
            hasTargetDependencies: true),
    ];

    private static readonly IReadOnlyDictionary<string, RenderPipelineBenchmarkSceneDefinition> s_byName
        = new ReadOnlyDictionary<string, RenderPipelineBenchmarkSceneDefinition>(
            s_all.ToDictionary(static scene => scene.Name, StringComparer.Ordinal));

    public static IReadOnlyList<RenderPipelineBenchmarkSceneDefinition> All { get; } = Array.AsReadOnly(s_all);

    public static RenderPipelineBenchmarkSceneDefinition Get(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return s_byName.TryGetValue(name, out RenderPipelineBenchmarkSceneDefinition? scene)
            ? scene
            : throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown render-pipeline benchmark scene.");
    }

    public static Half[] CreateLinearPremultipliedRgba16F(
        RenderPipelineBenchmarkSceneDefinition scene,
        PixelSize size)
    {
        ArgumentNullException.ThrowIfNull(scene);
        int length = GetRequiredComponentCount(size);
        var result = new Half[length];
        FillLinearPremultipliedRgba16F(result, scene, size);
        return result;
    }

    public static void FillLinearPremultipliedRgba16F(
        Span<Half> destination,
        RenderPipelineBenchmarkSceneDefinition scene,
        PixelSize size)
    {
        ArgumentNullException.ThrowIfNull(scene);
        int requiredLength = GetRequiredComponentCount(size);
        if (destination.Length != requiredLength)
        {
            throw new ArgumentException(
                $"Destination must contain exactly {requiredLength} RGBA components.", nameof(destination));
        }

        int index = 0;
        for (int y = 0; y < size.Height; y++)
        {
            for (int x = 0; x < size.Width; x++)
            {
                uint sample = MixPixel(scene.Seed, x, y);
                float alpha = (96 + ((sample >> 24) & 0x8f)) / 255f;
                float red = ((sample >> 16) & 0xff) / 255f * alpha;
                float green = ((sample >> 8) & 0xff) / 255f * alpha;
                float blue = (sample & 0xff) / 255f * alpha;

                destination[index++] = (Half)red;
                destination[index++] = (Half)green;
                destination[index++] = (Half)blue;
                destination[index++] = (Half)alpha;
            }
        }
    }

    private static int GetRequiredComponentCount(PixelSize size)
    {
        if (size.Width <= 0 || size.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size), size, "Benchmark dimensions must be positive.");
        }

        return checked(size.Width * size.Height * 4);
    }

    private static uint MixPixel(int seed, int x, int y)
    {
        unchecked
        {
            uint value = (uint)seed;
            value ^= (uint)(x / 16) * 0x9e37_79b9u;
            value ^= (uint)(y / 16) * 0x85eb_ca6bu;
            value ^= (uint)x * 0xc2b2_ae35u;
            value ^= (uint)y * 0x27d4_eb2fu;
            value ^= value >> 16;
            value *= 0x7feb_352du;
            value ^= value >> 15;
            value *= 0x846c_a68bu;
            return value ^ (value >> 16);
        }
    }
}
