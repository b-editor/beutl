using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Data.ObjectData;

namespace BEditor.Core.Data.PropertyData.EasingSetting {
    [DataContract(Namespace = "")]
    public abstract class EasingFunc : ComponentObject {
        private PropertyElement parent;
        protected object CreatedControl;


        /// <summary>
        /// IEasingSettingを実装するクラスをListにする
        /// </summary>
        public abstract IList<IEasingSetting> EasingSettings { get; }
        public ClipData ClipData => parent.ClipData;
        public PropertyElement Parent {
            get => parent;
            set {
                parent = value;
                var tmp = EasingSettings;
                var parent_ = parent.Parent;

                for (int i = 0; i < tmp.Count; i++) {
                    tmp[i].Parent = parent_;
                }
            }
        }

        public abstract float EaseFunc(in int frame, in int totalframe, in float min, in float max);

        public virtual void PropertyLoaded() {
            var settings = EasingSettings;

            void For1(int i) => settings[i].PropertyLoaded();
            Parallel.For(0, settings.Count, For1);

            //フィールドがpublicのときだけなので注意
            var attributetype = typeof(PropertyMetadataAttribute);
            var type = GetType();
            var properties = type.GetProperties();

            void For2(int i) {
                var property = properties[i];

                //metadata属性の場合&プロパティがPropertyElement
                if (Attribute.GetCustomAttribute(property, attributetype) is PropertyMetadataAttribute metadata &&
                                    property.GetValue(this) is PropertyElement propertyElement) {

                    propertyElement.PropertyMetadata = metadata.PropertyMetadata;
                }
            }
            Parallel.For(0, properties.Length, For2);
        }

        /// <summary>
        /// 読み込まれているイージング関数のType
        /// </summary>
        public static List<EasingData> LoadedEasingFunc { get; } = new List<EasingData>() {
            new EasingData() { Name = "デフォルト", Type = typeof(DefaultEasing) }
        };
    }

    public class EasingData {
        public string Name { get; set; }
        public Type Type { get; set; }
    }
}
