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
                foreach (var item in EasingSettings) {
                    item.Parent = parent.Parent;
                }
            }
        }

        public abstract float EaseFunc(int frame, int totalframe, float min, float max);

        public virtual void PropertyLoaded() {
            var settings = EasingSettings;

            Parallel.For(0, settings.Count, i => {
                settings[i].PropertyLoaded();
            });

            //フィールドがpublicのときだけなので注意
            var attributetype = typeof(PropertyMetadataAttribute);
            var type = GetType();
            var properties = type.GetProperties();

            Parallel.For(0, properties.Length, i => {
                var property = properties[i];

                //metadata属性の場合&プロパティがPropertyElement
                if (Attribute.GetCustomAttribute(property, attributetype) is PropertyMetadataAttribute metadata &&
                                    property.GetValue(this) is PropertyElement propertyElement) {

                    propertyElement.PropertyMetadata = metadata.PropertyMetadata;
                }
            });
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
