using System;
using System.Collections.Generic;
using System.Text;

using BEditor.NET.Data.EffectData;
using BEditor.NET.Data.ObjectData;

namespace BEditor.NET.Data.PropertyData.EasingSetting {
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
