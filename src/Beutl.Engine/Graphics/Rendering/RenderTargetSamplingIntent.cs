using SkiaSharp;

namespace Beutl.Graphics.Rendering;

internal readonly struct RenderTargetSamplingIntent
{
    private readonly RenderTargetSamplingIntentKind _kind;
    private readonly GRRecordingContext? _consumerContext;

    private RenderTargetSamplingIntent(
        RenderTargetSamplingIntentKind kind,
        GRRecordingContext? consumerContext = null)
    {
        _kind = kind;
        _consumerContext = consumerContext;
    }

    public static RenderTargetSamplingIntent CpuReadback => default;

    public static RenderTargetSamplingIntent BackendInterop { get; }
        = new(RenderTargetSamplingIntentKind.BackendInterop);

    public static RenderTargetSamplingIntent SameContextTextureSampling(GRRecordingContext? consumerContext)
        => new(RenderTargetSamplingIntentKind.SameContextTextureSampling, consumerContext);

    // Skia queues the read behind the submitted work and reports it once finished, so nothing waits here.
    public static RenderTargetSamplingIntent AsyncCpuReadback { get; }
        = new(RenderTargetSamplingIntentKind.AsyncCpuReadback);

    internal bool RequiresBackendInterop => _kind == RenderTargetSamplingIntentKind.BackendInterop;

    internal bool IsAsyncCpuReadback => _kind == RenderTargetSamplingIntentKind.AsyncCpuReadback;

    internal bool CanSubmitWithoutCompletion(GRRecordingContext? producerContext)
    {
        if (IsAsyncCpuReadback)
            return true;

        if (_kind != RenderTargetSamplingIntentKind.SameContextTextureSampling)
            return false;

        return producerContext is null
            ? _consumerContext is null
            : _consumerContext is not null && producerContext.Handle == _consumerContext.Handle;
    }
}
