using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditor.NET.Media;

namespace BEditor.NET.Data.PropertyData {
    /// <summary>
    /// 
    /// </summary>
    [DataContract(Namespace = "")]
    public class FontProperty : PropertyElement {
        private Font selectItem;

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
        public Font Select { get => selectItem; set => SetValue(value, ref selectItem, nameof(Select)); }


        public static implicit operator string(FontProperty fontProperty) => fontProperty.Select.Name;
        public override string ToString() => $"(Select:{Select} Name:{PropertyMetadata?.Name})";


        #region Commands

        /// <summary>
        /// 
        /// </summary>
        public class ChangeSelect : IUndoRedoCommand {
            private readonly FontProperty Selector;
            private readonly Font select;
            private readonly Font oldselect;

            /// <summary>
            /// 
            /// </summary>
            /// <param name="property"></param>
            /// <param name="select"></param>
            public ChangeSelect(FontProperty property, Font select) {
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

        public static readonly List<Font> FontList = new();

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
        public List<Font> ItemSource => FontProperty.FontList;
        /// <summary>
        /// 
        /// </summary>
        public Font SelectItem { get; private set; }
        /// <summary>
        /// 
        /// </summary>
        public string MemberPath => "Name";
    }
}
