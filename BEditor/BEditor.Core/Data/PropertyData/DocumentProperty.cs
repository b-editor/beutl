using System;
using System.Runtime.Serialization;

namespace BEditor.Core.Data.PropertyData {
    /// <summary>
    /// 
    /// </summary>
    [DataContract(Namespace = "")]
    public sealed class DocumentProperty : PropertyElement {
        private string textProperty;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="metadata"></param>
        public DocumentProperty(DocumentPropertyMetadata metadata) {
            Text = metadata.DefaultText;
            HeightProperty = metadata.Height;
            PropertyMetadata = metadata;
        }


        /// <summary>
        /// 
        /// </summary>
        [DataMember]
        public string Text { get => textProperty; set => SetValue(value, ref textProperty, nameof(Text)); }
        /// <summary>
        /// 
        /// </summary>
        public int? HeightProperty { get; set; }


        public override string ToString() => $"(Text:{Text})";

        #region Commands

        /// <summary>
        /// 
        /// </summary>
        public sealed class TextChangedCommand : IUndoRedoCommand {
            private readonly DocumentProperty Document;
            private readonly string newtext;
            private readonly string oldtext;

            /// <summary>
            /// 
            /// </summary>
            /// <param name="property"></param>
            /// <param name="text"></param>
            public TextChangedCommand(DocumentProperty property, string text) {
                Document = property;
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
