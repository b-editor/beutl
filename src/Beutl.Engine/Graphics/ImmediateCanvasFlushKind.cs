namespace Beutl.Graphics;

internal enum ImmediateCanvasFlushKind : byte
{
    CanvasClose,
    CanvasSubmit,
    SourceSurface,
    // Submit followed by a CPU completion wait.
    PrepareForSampling,
    // Submit only; the consumer is ordered in the same Skia context.
    PrepareForSamplingSubmit,
}
