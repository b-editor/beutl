using System;
using System.Collections.Generic;
using System.Text;

using BEditorCore.Data.EffectData;
using BEditorCore.Data.ObjectData;

namespace BEditorCore.Data.PropertyData.EasingSetting {
    public interface IEasingSetting {
        public EffectElement Parent { get; set; }
        public dynamic ComponentData { get; }

        /// <summary>
        /// ロード時の呼び出す
        /// </summary>
        public virtual void PropertyLoaded() {

        }
    }
}
