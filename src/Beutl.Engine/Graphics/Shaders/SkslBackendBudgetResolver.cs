using Beutl.Graphics.Effects;
using SkiaSharp;

namespace Beutl.Graphics.Shaders;

/// <summary>
/// Selects finite fusion budgets for the active Skia backend and the Vulkan-native lowering.
/// </summary>
/// <remarks>
/// SkiaSharp exposes the Skia backend family but not its fragment-uniform, sampler, or runtime-effect child
/// ceilings. These profiles are therefore conservative engine policies rather than exact driver limits.
/// <see cref="Portable"/> is the common-denominator profile used when target-less rasterization has not
/// allocated a backend surface yet. Backend-specific profiles may raise its limits but must not lower them.
/// Their capability classes remain part of backend program identities even when individual limits coincide.
/// Source and token limits bound fusion-generated program growth; a valid single stage remains eligible for
/// the compatibility path when it exceeds one of those limits. <see cref="SpirvVulkan"/> records the smaller
/// complete subset supported by the first native lowering in this same policy mechanism.
/// </remarks>
internal static class SkslBackendBudgetResolver
{
    private const int MaxStages = 16;
    private const int MaxUniformVectors = 128;
    private const int MaxSourceBytes = 64 * 1024;
    private const int MaxProgramTokens = 16 * 1024;

    private static readonly SkslBackendBudget s_portable = Create(SkslBackendCapabilityClass.Portable);
    private static readonly SkslBackendBudget s_vulkan = Create(SkslBackendCapabilityClass.Vulkan);
    private static readonly SkslBackendBudget s_metal = Create(SkslBackendCapabilityClass.Metal);
    private static readonly SkslBackendBudget s_spirvVulkan = Create(SkslBackendCapabilityClass.SpirvVulkan);

    public static SkslBackendBudget Portable => s_portable;

    /// <summary>
    /// Gets the initial Vulkan-native lowering budget. Native snippet fusion is not yet enabled, so one lowered
    /// stage is the complete supported program rather than an accidental unbounded subset.
    /// </summary>
    public static SkslBackendBudget SpirvVulkan => s_spirvVulkan;

    public static SkslBackendBudget Resolve(GRBackend? backend)
        => backend switch
        {
            GRBackend.Vulkan => s_vulkan,
            GRBackend.Metal => s_metal,
            _ => s_portable,
        };

    private static SkslBackendBudget Create(SkslBackendCapabilityClass capabilityClass)
    {
        // Supported GL, D3D, Vulkan, and Metal families guarantee at least 16 fragment samplers.
        // Reserve four for Skia's surrounding paint, blend, coverage, and clip program.
        // ProgramMetrics counts a sampler and child together, so both limits must match.
        (int maxSamplers, int maxChildren) = capabilityClass switch
        {
            SkslBackendCapabilityClass.Portable => (12, 12),
            SkslBackendCapabilityClass.Vulkan => (12, 12),
            SkslBackendCapabilityClass.Metal => (12, 12),
            SkslBackendCapabilityClass.SpirvVulkan => (1, 1),
            _ => throw new ArgumentOutOfRangeException(nameof(capabilityClass)),
        };

        int maxStages = capabilityClass == SkslBackendCapabilityClass.SpirvVulkan
            ? 1
            : MaxStages;
        // Vulkan guarantees 128 push-constant bytes. The native source mapping reserves one vec4, leaving seven
        // vec4 slots for description uniforms without relying on a larger device-specific limit.
        int maxUniformVectors = capabilityClass == SkslBackendCapabilityClass.SpirvVulkan
            ? 7
            : MaxUniformVectors;

        return new SkslBackendBudget(
            capabilityClass,
            maxStages,
            maxUniformVectors,
            maxSamplers,
            maxChildren,
            MaxSourceBytes,
            MaxProgramTokens);
    }
}
