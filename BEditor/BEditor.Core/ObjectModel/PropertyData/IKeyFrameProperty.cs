using System;
using System.Collections.Generic;
using System.Text;

using BEditor.ObjectModel.EffectData;
using BEditor.ObjectModel.ProjectData;

namespace BEditor.ObjectModel.PropertyData
{
    /// <summary>
    /// タイムライン上に編集画面を持つプロパティを表します
    /// </summary>
    public interface IKeyFrameProperty : IChild<EffectElement>
    {
        /// <summary>
        /// UIなどのキャッシュを入れる配列を取得します
        /// </summary>
        public Dictionary<string, dynamic> ComponentData { get; }
    }
}
