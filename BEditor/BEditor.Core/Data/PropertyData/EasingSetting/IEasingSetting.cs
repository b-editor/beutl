using System;
using System.Collections.Generic;
using System.Text;

using BEditor.Core.Data.EffectData;
using BEditor.Core.Data.ObjectData;

namespace BEditor.Core.Data.PropertyData.EasingSetting
{
    /// <summary>
    /// <see cref="EasingFunc"/> で利用可能なプロパティを表します
    /// </summary>
    public interface IEasingSetting
    {
        /// <summary>
        /// 親要素を取得します
        /// </summary>
        public EffectElement Parent { get; set; }
        /// <summary>
        /// UIなどのキャッシュを入れる配列を取得します
        /// </summary>
        public Dictionary<string, dynamic> ComponentData { get; }

        /// <summary>
        /// 初期化時とデシリアライズ時に呼び出されます
        /// </summary>
        public void PropertyLoaded();
    }
}
