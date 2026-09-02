using Beutl.Graphics.Backend;
using Beutl.Graphics.Effects;

namespace Beutl.Graphics.Rendering;

internal static class SpirvShaderProgramCache
{
    private const long DefaultRetainedByteBudget = 16 * 1024 * 1024;
    private const string ColorAlphaFormatContract = "linear-premultiplied-rgba16f";
    private static readonly object s_defaultCompileOptions = new();

    public static ProgramCache<GLSLFilterPipeline> Create()
        => new(
            resetRuntimeBindings: static _ => { },
            retainedByteSize: static program => program.RetainedByteSize,
            maxRetainedBytes: DefaultRetainedByteBudget,
            shareLeasedPrograms: true);

    public static ProgramCacheContextKey CreateContextKey(RenderCacheDeviceContextIdentity context)
    {
        context.ThrowIfUninitialized(nameof(context));
        return new ProgramCacheContextKey(
            context.DeviceIdentity,
            context.ContextIdentity,
            SkslBackendBudgetResolver.SpirvVulkan.CapabilityClass,
            ColorAlphaFormatContract,
            s_defaultCompileOptions);
    }

    public static ProgramCacheLease<GLSLFilterPipeline> Acquire(
        ProgramCache<GLSLFilterPipeline> cache,
        ShaderDescription description,
        IGraphicsContext graphicsContext,
        ProgramCacheContextKey context)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(graphicsContext);
        ArgumentNullException.ThrowIfNull(context);
        SpirvShaderLowering lowering = description.SpirvLowering
            ?? throw new ArgumentException("The shader description has no SPIR-V lowering.", nameof(description));
        ShaderProgramIdentity identity = ShaderProgramIdentity.CreateSpirv(
            description,
            lowering,
            SkslBackendBudgetResolver.SpirvVulkan);
        return cache.GetOrCreate(
            identity,
            context,
            new SpirvProgramCreationState(graphicsContext, lowering),
            static state => GLSLFilterPipeline.Create(
                    state.GraphicsContext,
                    state.Lowering.FragmentShaderSource,
                    ShaderOutputCoverage.ProvablyFull)
                ?? throw new InvalidOperationException("Failed to compile the SPIR-V shader program."));
    }

    private readonly record struct SpirvProgramCreationState(
        IGraphicsContext GraphicsContext,
        SpirvShaderLowering Lowering);
}
