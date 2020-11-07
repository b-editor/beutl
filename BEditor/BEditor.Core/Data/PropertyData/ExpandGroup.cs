using System;
using System.Runtime.Serialization;

using BEditor.Core.Data.PropertyData.EasingSetting;

namespace BEditor.Core.Data.PropertyData {
    /// <summary>
    /// 複数の <see cref="PropertyElement"/> をエクスパンダーでまとめるクラス
    /// </summary>
    [DataContract(Namespace = "")]
    public abstract class ExpandGroup : Group, IEasingSetting {
        private bool isOpen;

        /// <summary>
        /// エクスパンダーが開いているかを取得または設定します
        /// </summary>
        [DataMember]
        public bool IsExpanded { get => isOpen; set => SetValue(value, ref isOpen, nameof(IsExpanded)); }

        /// <summary>
        /// <see cref="ExpandGroup"/> クラスの新しいインスタンスを初期化します
        /// </summary>
        /// <param name="metadata">このプロパティの <see cref="PropertyElementMetadata"/></param>
        /// <exception cref="ArgumentNullException"><paramref name="metadata"/> が <see langword="null"/> です</exception>
        public ExpandGroup(PropertyElementMetadata metadata) {
            PropertyMetadata = metadata??throw new ArgumentNullException(nameof(metadata));
        }

        /// <inheritdoc/>
        public override string ToString() => $"(IsExpanded:{IsExpanded} Name:{PropertyMetadata?.Name})";
    }
}
