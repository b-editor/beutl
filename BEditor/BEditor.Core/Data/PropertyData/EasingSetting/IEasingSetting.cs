using System;
using System.Collections.Generic;
using System.Text;

using BEditor.Core.Data.EffectData;
using BEditor.Core.Data.ObjectData;

namespace BEditor.Core.Data.PropertyData.EasingSetting {
    public interface IEasingSetting {
        public EffectElement Parent { get; set; }
        public Dictionary<string, dynamic> ComponentData { get; }

        /// <summary>
        /// ロード時の呼び出す
        /// </summary>
        public virtual void PropertyLoaded() {

        }
    }
}
