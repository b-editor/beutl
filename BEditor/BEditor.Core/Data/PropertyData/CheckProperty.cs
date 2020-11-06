using System;
using System.Runtime.Serialization;

using BEditor.Core.Data.PropertyData.EasingSetting;

namespace BEditor.Core.Data.PropertyData {
    /// <summary>
    /// チェックボックスのプロパティクラス
    /// </summary>
    [DataContract(Namespace = "")]
    public sealed class CheckProperty : PropertyElement, IEasingSetting {
        private bool isChecked;

        /// <summary>
        /// <see cref="CheckProperty"/> クラスの新しいインスタンスを初期化します
        /// </summary>
        /// <param name="metadata">このプロパティの <see cref="CheckPropertyMetadata"/></param>
        /// <exception cref="ArgumentNullException"><paramref name="metadata"/> が <see langword="null"/> です</exception>
        public CheckProperty(CheckPropertyMetadata metadata) {
            if (metadata is null) throw new ArgumentNullException(nameof(metadata));

            PropertyMetadata = metadata;
            isChecked = metadata.DefaultIsChecked;
        }

        /// <summary>
        /// チェックされている場合 <see langword="true"/>、そうでない場合は <see langword="false"/> となります
        /// </summary>
        [DataMember]
        public bool IsChecked { get => isChecked; set => SetValue(value, ref isChecked, nameof(IsChecked)); }

        /// <inheritdoc/>
        public override string ToString() => $"(IsChecked:{IsChecked} Name:{PropertyMetadata?.Name})";

        /// <summary>
        /// チェックされているかを変更するコマンド
        /// </summary>
        /// <remarks>このクラスは <see cref="UndoRedoManager.Do(IUndoRedoCommand)"/> と併用することでコマンドを記録できます</remarks>
        public sealed class ChangeChecked : IUndoRedoCommand {
            private readonly CheckProperty CheckSetting;
            private readonly bool value;

            /// <summary>
            /// <see cref="ChangeChecked"/> クラスの新しいインスタンスを初期化します
            /// </summary>
            /// <param name="property">対象の <see cref="CheckProperty"/></param>
            /// <param name="value">新しい値</param>
            /// <exception cref="ArgumentNullException"><paramref name="property"/> が <see langword="null"/> です</exception>
            public ChangeChecked(CheckProperty property, bool value) {
                CheckSetting = property ?? throw new ArgumentNullException(nameof(property));
                this.value = value;
            }

            /// <inheritdoc/>
            public void Do() => CheckSetting.IsChecked = value;

            /// <inheritdoc/>
            public void Redo() => Do();

            /// <inheritdoc/>
            public void Undo() => CheckSetting.IsChecked = !value;
        }
    }

    /// <summary>
    /// <see cref="CheckProperty"/> のメタデータ
    /// </summary>
    public class CheckPropertyMetadata : PropertyElementMetadata {
        /// <summary>
        /// <see cref="CheckPropertyMetadata"/> クラスの新しいインスタンスを初期化します
        /// </summary>
        public CheckPropertyMetadata(string name, bool defaultvalue = false) : base(name) => DefaultIsChecked = defaultvalue;

        /// <summary>
        /// デフォルトの値を取得します
        /// </summary>
        public bool DefaultIsChecked { get; private set; }
    }
}
