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

    internal bool RequiresBackendInterop => _kind == RenderTargetSamplingIntentKind.BackendInterop;

    internal bool CanSubmitWithoutCompletion(GRRecordingContext? producerContext)
    {
        if (_kind != RenderTargetSamplingIntentKind.SameContextTextureSampling)
            return false;

        return producerContext is null
            ? _consumerContext is null
            : _consumerContext is not null && producerContext.Handle == _consumerContext.Handle;
    }
}
