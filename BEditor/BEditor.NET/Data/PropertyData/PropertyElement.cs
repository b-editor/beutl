using System;
using System.Runtime.Serialization;

using BEditor.NET.Data.EffectData;
using BEditor.NET.Data.ObjectData;
using BEditor.NET.Data.ProjectData;

namespace BEditor.NET.Data.PropertyData {
    /// <summary>
    /// プロパティのベースクラス
    /// </summary>
    [DataContract(Namespace = "")]
    public abstract class PropertyElement : ComponentObject {
        /// <summary>
        /// 
        /// </summary>
        public virtual EffectElement Parent { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public ClipData ClipData => Parent.ClipData;
        /// <summary>
        /// 
        /// </summary>
        public Scene Scene => ClipData.Scene;


        private PropertyElementMetadata propertyMetadata;

        /// <summary>
        /// 
        /// </summary>
        public PropertyElementMetadata PropertyMetadata { get => propertyMetadata; set => SetValue(value, ref propertyMetadata, nameof(PropertyMetadata)); }

        /// <summary>
        /// 初期化時とデシリアライズ時に呼び出される
        /// </summary>
        public virtual void PropertyLoaded() {

        }

        public override string ToString() => $"(Name:{PropertyMetadata?.Name})";
    }

    /// <summary>
    /// 
    /// </summary>
    public class PropertyElementMetadata {

        /// <summary>
        /// 
        /// </summary>
        /// <param name="name"></param>
        public PropertyElementMetadata(string name) => Name = name;

        /// <summary>
        /// 
        /// </summary>
        public string Name { get; }
    }
}
