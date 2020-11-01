using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Threading.Tasks;

using BEditor.Core.Data.EffectData;
using BEditor.Core.Data.ObjectData;
using BEditor.Core.Data.PropertyData.EasingSetting;

namespace BEditor.Core.Data.PropertyData {
    [DataContract(Namespace = "")]
    public abstract class Group : PropertyElement, IKeyFrameProperty, IEasingSetting {
        protected object CreatedKeyFrame;
        protected EffectElement parent;

        public abstract IList<PropertyElement> GroupItems { get; }

        public override EffectElement Parent {
            get => parent;
            set {
                parent = value;

                for (int i = 0; i < GroupItems.Count; i++) {
                    GroupItems[i].Parent = value;
                }
            }
        }


        public override void PropertyLoaded() {
            base.PropertyLoaded();

            var g = GroupItems;
            void For1(int index) => g[index].PropertyLoaded();

            Parallel.For(0, GroupItems.Count, For1);

            //フィールドがpublicのときだけなので注意
            var attributetype = typeof(PropertyMetadataAttribute);
            var type = GetType();
            var properties = type.GetProperties();

            void For2(int index) {
                var property = properties[index];

                //metadata属性の場合&プロパティがPropertyElement
                if (Attribute.GetCustomAttribute(property, attributetype) is PropertyMetadataAttribute metadata &&
                                    property.GetValue(this) is PropertyElement propertyElement) {

                    propertyElement.PropertyMetadata = metadata.PropertyMetadata;
                }
            }

            Parallel.For(0, properties.Length, For2);
        }
    }
}
