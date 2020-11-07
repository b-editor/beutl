using System;
using System.Collections.Generic;
using System.Text;

using BEditor.Core.Data.ProjectData;

namespace BEditor.Core.Data.PropertyData {
    /// <summary>
    /// タイムライン上に編集画面を持つプロパティを表します
    /// </summary>
    public interface IKeyFrameProperty {
        /// <summary>
        /// シーンを取得します
        /// </summary>
        public Scene Scene { get; }
        /// <summary>
        /// UIなどのキャッシュを入れる配列を取得します
        /// </summary>
        public Dictionary<string, dynamic> ComponentData { get; }
    }
}
