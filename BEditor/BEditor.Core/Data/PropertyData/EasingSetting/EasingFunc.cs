using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Data.ObjectData;

namespace BEditor.Core.Data.PropertyData.EasingSetting {
    /// <summary>
    /// <see cref="EaseProperty"/>, <see cref="ColorAnimationProperty"/> などで利用可能なイージング関数を表します
    /// </summary>
    [DataContract(Namespace = "")]
    public abstract class EasingFunc : ComponentObject {
        private PropertyElement parent;


        /// <summary>
        /// UIに表示するプロパティを取得します
        /// </summary>
        public abstract IList<IEasingSetting> EasingSettings { get; }
        /// <summary>
        /// 親要素を取得します
        /// </summary>
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

        /// <summary>
        /// イージング関数
        /// </summary>
        /// <param name="frame">取得するフレーム</param>
        /// <param name="totalframe">全体のフレーム</param>
        /// <param name="min">最小の値</param>
        /// <param name="max">最大の値</param>
        /// <returns>イージングされた値</returns>
        public abstract float EaseFunc(int frame, int totalframe, float min, float max);

        /// <summary>
        /// 初期化時とデシリアライズ時に呼び出されます
        /// </summary>
        public virtual void PropertyLoaded() {
            var settings = EasingSettings;

            settings.AsParallel().ForAll(setting => setting.PropertyLoaded());

            //フィールドがpublicのときだけなので注意
            var attributetype = typeof(PropertyMetadataAttribute);
            var type = GetType();
            var properties = type.GetProperties();

            properties.AsParallel().ForAll(property => {
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
