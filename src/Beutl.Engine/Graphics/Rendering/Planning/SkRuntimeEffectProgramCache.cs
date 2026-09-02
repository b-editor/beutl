using System.Runtime.CompilerServices;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Requests;
using SkiaSharp;

namespace Beutl.Graphics.Shaders;

internal static class SkRuntimeEffectProgramCache
{
    private const long DefaultRetainedByteBudget = 16 * 1024 * 1024;
    private const string ColorAlphaFormatContract = "linear-premultiplied-rgba16f";
    private static readonly object s_cpuDestinationContext = new();
    private static readonly object s_defaultCompileOptions = new();
    private static readonly ConditionalWeakTable<GRRecordingContext, object> s_destinationContextIdentities = new();

    public static ProgramCache<CachedSkRuntimeEffect> Create()
        => new(
            resetRuntimeBindings: static _ => { },
            retainedByteSize: static program => program.RetainedBytes,
            maxRetainedBytes: DefaultRetainedByteBudget,
            shareLeasedPrograms: true);

    public static ProgramCacheContextKey CreateContextKey(
        RenderCacheDeviceContextIdentity context,
        SkslBackendBudget budget)
    {
        ArgumentNullException.ThrowIfNull(budget);
        context.ThrowIfUninitialized(nameof(context));
        return new ProgramCacheContextKey(
            context.DeviceIdentity,
            context.ContextIdentity,
            budget.CapabilityClass,
            ColorAlphaFormatContract,
            s_defaultCompileOptions);
    }

    public static ProgramCacheLease<CachedSkRuntimeEffect> Acquire(
        ProgramCache<CachedSkRuntimeEffect> cache,
        string source,
        SkslBackendBudget budget,
        ProgramCacheContextKey context)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(context);
        ShaderProgramIdentity identity = ShaderProgramIdentity.CreateStandaloneSksl(
            source,
            budget);
        return cache.GetOrCreate(
            identity,
            context,
            source,
            static value => CachedSkRuntimeEffect.Create(value));
    }

    public static ProgramCacheLease<CachedSkRuntimeEffect> AcquireForDestination(
        ProgramCache<CachedSkRuntimeEffect> cache,
        RenderTarget destination,
        string source)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(destination);
        destination.VerifyAccess();
        GRRecordingContext? graphicsContext = destination.RawValue.Context;
        object contextIdentity = graphicsContext is null
            ? s_cpuDestinationContext
            : s_destinationContextIdentities.GetValue(
                graphicsContext,
                static _ => new object());
        cache.SynchronizeContext(cache, contextIdentity);
        SkslBackendBudget budget = SkslBackendBudgetResolver.Resolve(graphicsContext?.Backend);
        ProgramCacheContextKey contextKey = CreateContextKey(
            new RenderCacheDeviceContextIdentity(cache, contextIdentity),
            budget);
        return Acquire(cache, source, budget, contextKey);
    }
}
