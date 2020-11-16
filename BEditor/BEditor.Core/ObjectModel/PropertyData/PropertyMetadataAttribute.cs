using System;
using System.Reflection;
using System.Threading.Tasks;

namespace BEditor.ObjectModel.PropertyData
{
    /// <summary>
    /// <see cref="PropertyElement.PropertyLoaded"/> で自動で <see cref="PropertyElementMetadata"/> を設定します
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public class PropertyMetadataAttribute : Attribute
    {
        /// <summary>
        /// <see cref="PropertyElementMetadata"/> を取得します
        /// </summary>
        public PropertyElementMetadata PropertyMetadata { get; }

        /// <summary>
        /// <see cref="PropertyMetadataAttribute"/> クラスの新しいインスタンスを初期化します
        /// </summary>
        /// <param name="Fieldpath"><see cref="PropertyElementMetadata"/> の静的フィールドの名前</param>
        /// <param name="Type"><paramref name="Fieldpath"/> があるクラスの <see cref="Type"/></param>
        public PropertyMetadataAttribute(string Fieldpath, Type Type)
        {
            var info = Type.GetField(Fieldpath, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

            if (info != null && info.IsStatic)
            {
                PropertyMetadata = info.GetValue(null) as PropertyElementMetadata;
            }
        }
    }
}
