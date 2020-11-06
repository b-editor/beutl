using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditor.Core.Media;

namespace BEditor.Core.Data.PropertyData {
    /// <summary>
    /// 
    /// </summary>
    [DataContract(Namespace = "")]
    public sealed class FontProperty : PropertyElement {
        private FontRecord selectItem;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="metadata"></param>
        public FontProperty(FontPropertyMetadata metadata) {
            selectItem = metadata.SelectItem;
            PropertyMetadata = metadata;
        }


        /// <summary>
        /// 
        /// </summary>
        [DataMember]
        public FontRecord Select { get => selectItem; set => SetValue(value, ref selectItem, nameof(Select)); }


        public static implicit operator string(FontProperty fontProperty) => fontProperty.Select.Name;
        public override string ToString() => $"(Select:{Select} Name:{PropertyMetadata?.Name})";


        #region Commands

        /// <summary>
        /// 
        /// </summary>
        public sealed class ChangeSelect : IUndoRedoCommand {
            private readonly FontProperty Selector;
            private readonly FontRecord select;
            private readonly FontRecord oldselect;

            /// <summary>
            /// 
            /// </summary>
            /// <param name="property"></param>
            /// <param name="select"></param>
            public ChangeSelect(FontProperty property, FontRecord select) {
                Selector = property;
                this.select = select;
                oldselect = property.Select;
            }


            /// <inheritdoc/>
            public void Do() => Selector.Select = select;

            /// <inheritdoc/>
            public void Redo() => Do();

            /// <inheritdoc/>
            public void Undo() => Selector.Select = oldselect;
        }

        #endregion

        #region StaticMember

        public static readonly List<FontRecord> FontList = new();

        public static readonly List<string> FontStylesList = new() {
            Properties.Resources.FontStyle_Normal,
            Properties.Resources.FontStyle_Bold,
            Properties.Resources.FontStyle_Italic,
            Properties.Resources.FontStyle_UnderLine,
            Properties.Resources.FontStyle_StrikeThrough
        };

        #endregion
    }

    /// <summary>
    /// 
    /// </summary>
    public class FontPropertyMetadata : PropertyElementMetadata {
        /// <summary>
        /// 
        /// </summary>
        public FontPropertyMetadata() : base(Properties.Resources.Font) {
        }

        /// <summary>
        /// 
        /// </summary>
        public List<FontRecord> ItemSource => FontProperty.FontList;
        /// <summary>
        /// 
        /// </summary>
        public FontRecord SelectItem { get; private set; }
        /// <summary>
        /// 
        /// </summary>
        public string MemberPath => "Name";
    }
}
