using Beutl.Graphics.Effects;
using SkiaSharp;

namespace Beutl.Graphics.Rendering;

internal enum SkslBackendCapabilityClass : byte
{
    Portable,
    Vulkan,
    Metal,
}

/// <summary>
/// Selects a finite fusion budget for the active Skia backend.
/// </summary>
/// <remarks>
/// SkiaSharp exposes the backend family but not its fragment-uniform, sampler, or runtime-effect child
/// ceilings. These profiles are therefore conservative engine policies rather than exact driver limits.
/// <see cref="Portable"/> is the common-denominator profile used when target-less rasterization has not
/// allocated a backend surface yet. Backend-specific profiles may raise its limits but must not lower them.
/// Their capability classes remain part of backend program identities even when individual limits coincide.
/// Source and token limits bound fusion-generated program growth; a valid single stage remains eligible for
/// the compatibility path when it exceeds one of those limits.
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

    public static SkslBackendBudget Portable => s_portable;

    public static SkslBackendBudget Resolve(GRBackend? backend)
        => backend switch
        {
            GRBackend.Vulkan => s_vulkan,
            GRBackend.Metal => s_metal,
            _ => s_portable,
        };

    private static SkslBackendBudget Create(SkslBackendCapabilityClass capabilityClass)
    {
        // Portable also covers target-less rendering and backends whose family Skia cannot identify. There is no
        // universal sampler floor for an arbitrary unidentified driver, so this policy is deliberately based on
        // the common floor of Beutl's supported backend families rather than claiming one. OpenGL ES 3.0.6 table
        // 6.32 and OpenGL 4.6 core table 23.61 both require at least 16 fragment texture-image units; D3D exposes
        // at least 16 fragment-stage sampler slots as well. Vulkan 1.0 requires at least 16
        // maxPerStageDescriptorSamplers and 16 maxPerStageDescriptorSampledImages (Vulkan core specification,
        // Required Limits table). Apple's Metal Feature Set Tables likewise guarantee at least 16 sampler-state
        // argument-table entries per graphics function.
        //
        // Do not spend that entire guaranteed floor here: Skia composes a runtime effect into a larger fragment
        // program with paint, blend, coverage, and clip-mask resources that ProgramMetrics cannot see. Twelve
        // reserves four guaranteed slots for that surrounding program. A genuinely unsupported backend remains
        // outside this supported-family guarantee even when it reaches the Portable profile.
        //
        // ProgramMetrics increments samplers and children together for every resource, starting with one of each
        // for the implicit source. Their effective limit is therefore always the smaller value. Keep both at 12
        // instead of advertising Metal's larger texture-table limit, which this accounting model cannot exercise.
        (int maxSamplers, int maxChildren) = capabilityClass switch
        {
            SkslBackendCapabilityClass.Portable => (12, 12),
            SkslBackendCapabilityClass.Vulkan => (12, 12),
            SkslBackendCapabilityClass.Metal => (12, 12),
            _ => throw new ArgumentOutOfRangeException(nameof(capabilityClass)),
        };

        return new SkslBackendBudget(
            capabilityClass,
            MaxStages,
            MaxUniformVectors,
            maxSamplers,
            maxChildren,
            MaxSourceBytes,
            MaxProgramTokens);
    }
}
