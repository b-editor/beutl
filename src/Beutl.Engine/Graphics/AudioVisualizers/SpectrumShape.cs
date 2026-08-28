using Beutl.Engine;

namespace Beutl.Graphics.AudioVisualizers;

public abstract partial class SpectrumShape : EngineObject
{
    public SpectrumShape()
    {
        ScanProperties<SpectrumShape>();
    }

    public abstract partial class Resource
    {
        /// <summary>
        /// Paints one frame of the spectrum.
        /// </summary>
        /// <param name="context">
        /// The canvas, bounds, per-bar intensities and fill brush for this frame. Its spans are valid only
        /// until this call returns.
        /// </param>
        protected internal abstract void Render(in SpectrumRenderContext context);
    }
}
