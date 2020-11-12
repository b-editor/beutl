using System.Collections.Generic;

using BEditor.Core.Data.EffectData;
using BEditor.Core.Data.ObjectData;

namespace BEditor.Core.Data.ProjectData {
    /// <summary>
    /// フレーム描画時にクリップに渡されるデータ
    /// </summary>
    public class ClipRenderArgs {
        /// <summary>
        /// <see cref="ClipRenderArgs"/> クラスの新しいインスタンスを初期化します
        /// </summary>
        /// <param name="frame">タイムライン基準のフレーム</param>
        /// <param name="schedules">クリップのリスト</param>
        public ClipRenderArgs(int frame, List<ClipData> schedules) {
            Frame = frame;
            Schedules = schedules;
        }

        /// <summary>
        /// タイムライン基準のフレームを取得します
        /// </summary>
        public int Frame { get; }
        /// <summary>
        /// 読み込むクリップのリストを取得します
        /// </summary>
        public List<ClipData> Schedules { get; }
        /// <summary>
        /// 処理の現在の状態を示す値を取得または設定します
        /// </summary>
        public bool Handled { get; set; }
    }

    /// <summary>
    /// フレーム描画時にエフェクトに渡されるデータ
    /// </summary>
    public class EffectRenderArgs {
        /// <summary>
        /// <see cref="EffectRenderArgs"/> クラスの新しいインスタンスを初期化します
        /// </summary>
        /// <param name="frame">タイムライン基準のフレーム</param>
        /// <param name="schedules">エフェクトのリスト</param>
        public EffectRenderArgs(int frame, List<EffectElement> schedules) {
            Frame = frame;
            Schedules = schedules;
        }

        /// <summary>
        /// タイムライン基準のフレームを取得します
        /// </summary>
        public int Frame { get; }
        /// <summary>
        /// 読み込むエフェクトのリストを取得します
        /// </summary>
        public List<EffectElement> Schedules { get; }
        /// <summary>
        /// 処理の現在の状態を示す値を取得または設定します
        /// </summary>
        public bool Handled { get; set; }
    }
}
