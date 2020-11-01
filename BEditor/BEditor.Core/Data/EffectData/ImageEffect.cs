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
        /// エフェクト描画時に呼び出されます
        /// </summary>
        public abstract void Draw(ref Image source, EffectLoadArgs args);

        public override void Load(EffectLoadArgs args) {

        }
    }
}
