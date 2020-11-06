using System;
using System.Runtime.Serialization;

namespace BEditor.Core.Data.PropertyData {
    /// <summary>
    /// 複数行の文字のプロパティクラス
    /// </summary>
    [DataContract(Namespace = "")]
    public sealed class DocumentProperty : PropertyElement {
        private string textProperty;

        /// <summary>
        /// <see cref="DocumentProperty"/> クラスの新しいインスタンスを初期化します
        /// </summary>
        /// <param name="metadata">このプロパティの <see cref="DocumentPropertyMetadata"/></param>
        /// <exception cref="ArgumentNullException"><paramref name="metadata"/> が <see langword="null"/> です</exception>
        public DocumentProperty(DocumentPropertyMetadata metadata) {
            if (metadata is null) throw new ArgumentNullException(nameof(metadata));

            Text = metadata.DefaultText;
            HeightProperty = metadata.Height;
            PropertyMetadata = metadata;
        }


        /// <summary>
        /// 入力されている文字列を取得または設定します
        /// </summary>
        [DataMember]
        public string Text { get => textProperty; set => SetValue(value, ref textProperty, nameof(Text)); }
        /// <summary>
        /// 
        /// </summary>
        public int? HeightProperty { get; set; }

        /// <inheritdoc/>
        public override string ToString() => $"(Text:{Text} Name:{PropertyMetadata?.Name})";

        #region Commands

        /// <summary>
        /// 文字を変更するコマンド
        /// </summary>
        /// <remarks>このクラスは <see cref="UndoRedoManager.Do(IUndoRedoCommand)"/> と併用することでコマンドを記録できます</remarks>
        public sealed class TextChangedCommand : IUndoRedoCommand {
            private readonly DocumentProperty Document;
            private readonly string newtext;
            private readonly string oldtext;

            /// <summary>
            /// <see cref="TextChangedCommand"/> クラスの新しいインスタンスを初期化します
            /// </summary>
            /// <param name="property"></param>
            /// <param name="text"></param>
            /// <exception cref="ArgumentNullException"><paramref name="property"/> が <see langword="null"/> です</exception>
            public TextChangedCommand(DocumentProperty property, string text) {
                Document = property ?? throw new ArgumentNullException(nameof(property));
                newtext = text;
                oldtext = property.Text;
            }


            /// <inheritdoc/>
            public void Do() => Document.Text = newtext;

            /// <inheritdoc/>
            public void Redo() => Do();

            /// <inheritdoc/>
            public void Undo() => Document.Text = oldtext;
        }

        #endregion
    }
    //TODO : xmlドキュメント ここまで
    /// <summary>
    /// 
    /// </summary>
    public class DocumentPropertyMetadata : PropertyElementMetadata {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="text"></param>
        public DocumentPropertyMetadata(string text) : base("") => DefaultText = text;
        /// <summary>
        /// 
        /// </summary>
        /// <param name="text"></param>
        /// <param name="height"></param>
        public DocumentPropertyMetadata(string text, int? height) : base("") {
            DefaultText = text;
            Height = height;
        }

        /// <summary>
        /// 
        /// </summary>
        public string DefaultText { get; private set; }
        /// <summary>
        /// 
        /// </summary>
        public int? Height { get; private set; }
    }
}
