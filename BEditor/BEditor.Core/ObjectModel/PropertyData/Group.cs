using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;

using BEditor.ObjectModel.EffectData;
using BEditor.ObjectModel.ObjectData;
using BEditor.ObjectModel.PropertyData.EasingSetting;

namespace BEditor.ObjectModel.PropertyData
{
    /// <summary>
    /// <see cref="PropertyElement"/> をまとめるクラス
    /// </summary>
    [DataContract(Namespace = "")]
    public abstract class Group : PropertyElement, IKeyFrameProperty, IEasingSetting, INotifyPropertyChanged, IExtensibleDataObject, IChild<EffectElement>, IParent<PropertyElement>
    {
        private IEnumerable<PropertyElement> cachedlist;
        
        /// <summary>
        /// グループにする <see cref="PropertyElement"/> を取得します
        /// </summary>
        public abstract IEnumerable<PropertyElement> Properties { get; }
        /// <summary>
        /// キャッシュされた <see cref="Properties"/> を取得します
        /// </summary>
        public IEnumerable<PropertyElement> Children => cachedlist ??= Properties;

        /// <inheritdoc/>
        public override EffectElement Parent
        {
            get => base.Parent;
            set
            {
                base.Parent = value;

                Parallel.ForEach(Children, item => item.Parent = value);
            }
        }

        /// <inheritdoc/>
        public override void PropertyLoaded()
        {
            base.PropertyLoaded();

            Parallel.ForEach(Children, item => item.PropertyLoaded());

            //TODO : ソースジェネレーターへ移行
            var attributetype = typeof(PropertyMetadataAttribute);
            var type = GetType();
            var properties = type.GetProperties();

            Parallel.ForEach(properties, property =>
            {
                //metadata属性の場合&プロパティがPropertyElement
                if (Attribute.GetCustomAttribute(property, attributetype) is PropertyMetadataAttribute metadata &&
                                    property.GetValue(this) is PropertyElement propertyElement)
                {

                    propertyElement.PropertyMetadata = metadata.PropertyMetadata;
                }
            });
        }
    }
}
