using Beutl.Engine;

namespace Beutl.Graphics.AudioVisualizers;

public abstract partial class WaveformShape : EngineObject
{
    public WaveformShape()
    {
        ScanProperties<WaveformShape>();
    }

    public abstract partial class Resource
    {
        /// <summary>
        /// Paints one frame of the waveform.
        /// </summary>
        /// <param name="context">
        /// The canvas, bounds, per-slot minimum and maximum samples, gain and fill brush for this frame.
        /// The samples arrive unscaled, so the implementation applies the gain and clamps back into -1..1.
        /// Its spans are valid only until this call returns.
        /// </param>
        protected internal abstract void Render(in WaveformRenderContext context);
    }
}
