using Beutl.Engine;
using Beutl.Media;

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
        /// 形状を描画する。normalizedBars は 0..1 に正規化済みのバーごとの強度。
        /// </summary>
        internal abstract void Render(
            ImmediateCanvas canvas,
            Rect bounds,
            ReadOnlySpan<float> normalizedBars,
            SolidColorBrush.Resource foregroundBrush,
            Color foregroundColor);
    }
}
