using System.Runtime.Serialization;

using BEditor.Core.Data.ProjectData;
using BEditor.Core.Media;

namespace BEditor.Core.Data.EffectData {
    /// <summary>
    /// 画像エフェクトのベースクラス
    /// </summary>
    [DataContract(Namespace = "")]
    public abstract class ImageEffect : EffectElement {
        /// <summary>
        /// フレーム描画時に呼び出されます
        /// </summary>
        /// <param name="image">描画する<see cref="Image"/></param>
        /// <param name="args">呼び出しの順番などのデータ</param>
        public abstract void Draw(ref Image image, EffectRenderArgs args);

        /// <inheritdoc/>
        public override void Render(EffectRenderArgs args) { }
    }
}
