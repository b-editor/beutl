using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;

using BEditor.Core.Data.EffectData;
using BEditor.Core.Data.ObjectData;
using BEditor.Core.Data.ProjectData;

namespace BEditor.Core.Data.PropertyData
{
    //Memo : xml英語ここまで
    /// <summary>
    /// Represents the property used by <see cref="EffectElement"/>.
    /// </summary>
    [DataContract(Namespace = "")]
    public abstract class PropertyElement : ComponentObject, IChild<EffectElement>, IExtensibleDataObject, INotifyPropertyChanged, IPropertyElement, IHadId
    {
        private static readonly PropertyChangedEventArgs metadataArgs = new(nameof(PropertyMetadata));
        private PropertyElementMetadata propertyMetadata;
        private int? id;


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
        /// <inheritdoc/>
        public int Id => id ??= Parent.Children.ToList().IndexOf(this);

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
