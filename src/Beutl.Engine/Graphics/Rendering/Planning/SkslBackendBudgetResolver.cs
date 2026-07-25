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
/// Source and token limits bound fusion-generated program growth; a valid single stage remains eligible for
/// the compatibility path when it exceeds one of those limits.
/// </remarks>
internal static class SkslBackendBudgetResolver
{
    private const int MaxStages = 16;
    private const int MaxUniformVectors = 128;
    private const int MaxSamplers = 8;
    private const int MaxChildren = 8;
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
        => new(
            capabilityClass,
            MaxStages,
            MaxUniformVectors,
            MaxSamplers,
            MaxChildren,
            MaxSourceBytes,
            MaxProgramTokens);
}
