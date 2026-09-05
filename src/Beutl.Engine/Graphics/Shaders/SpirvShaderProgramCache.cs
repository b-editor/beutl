using Beutl.Graphics.Backend;
using Beutl.Graphics.Backend.Composite;
using Beutl.Graphics.Backend.Vulkan;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Requests;

namespace Beutl.Graphics.Shaders;

internal static class SpirvShaderProgramCache
{
    private const long DefaultRetainedByteBudget = 16 * 1024 * 1024;
    private const string ColorAlphaFormatContract = "linear-premultiplied-rgba16f";
    private static readonly object s_defaultCompileOptions = new();

    public static ProgramCache<GLSLFilterPipeline> Create()
        => new(
            retainedByteSize: static program => program.RetainedByteSize,
            maxRetainedBytes: DefaultRetainedByteBudget);

    public static bool SupportsExecution(IGraphicsContext? context)
        => context is VulkanContext { Supports3DRendering: true }
            or CompositeContext { Supports3DRendering: true };

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
        if (!SupportsExecution(graphicsContext))
        {
            throw new ArgumentException(
                "SPIR-V shader execution requires the engine Vulkan recording context.",
                nameof(graphicsContext));
        }
        SpirvShaderLowering lowering = description.SpirvLowering
            ?? throw new ArgumentException("The shader description has no SPIR-V lowering.", nameof(description));
        return cache.GetOrCreate(
            lowering.ProgramIdentity,
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
