using System;
using System.Collections.Generic;
using System.Text;

using BEditor.Core.Data.Control;

namespace BEditor.Core.Data.Property
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
