using System;
using System.ComponentModel;
using System.Runtime.Serialization;

using BEditor.Core.Data.EffectData;
using BEditor.Core.Data.ObjectData;
using BEditor.Core.Data.ProjectData;

namespace BEditor.Core.Data.PropertyData
{
    /// <summary>
    /// 編集画面を持つプロパティを表します
    /// </summary>
    [DataContract(Namespace = "")]
    public abstract class PropertyElement : ComponentObject, IChild<EffectElement>, IExtensibleDataObject, INotifyPropertyChanged
    {
        private static readonly PropertyChangedEventArgs metadataArgs = new(nameof(PropertyMetadata));
        private PropertyElementMetadata propertyMetadata;


        /// <summary>
        /// このプロパティの親要素を取得します
        /// </summary>
        public virtual EffectElement Parent { get; set; }
        /// <summary>
        /// プロパティのメタデータを取得または設定します
        /// </summary>
        public PropertyElementMetadata PropertyMetadata
        { 
            get => propertyMetadata;
            set => SetValue(value, ref propertyMetadata, metadataArgs);
        }
        /// <summary>
        /// 初期化時とデシリアライズ時に呼び出されます
        /// </summary>
        public virtual void PropertyLoaded()
        {

        }

        /// <inheritdoc/>
        public override string ToString() => $"(Name:{PropertyMetadata?.Name})";
    }

    /// <summary>
    /// <see cref="PropertyElement"/> のメタデータを表します
    /// </summary>
    public record PropertyElementMetadata(string Name);
}
