using Beutl.Engine;
using Beutl.Media;

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
        /// 波形を描画する。mins / maxs は各スロットの最小値・最大値（-1..1、gain 未適用）。
        /// Shape 実装側で gain の適用と -1..1 へのクランプを行う。
        /// </summary>
        internal abstract void Render(
            ImmediateCanvas canvas,
            Rect bounds,
            ReadOnlySpan<float> mins,
            ReadOnlySpan<float> maxs,
            float gain,
            Brush.Resource fill);
    }
}
