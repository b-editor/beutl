using System;
using System.Collections;
using System.Runtime.Serialization;

using BEditorCore.Data.PropertyData.EasingSetting;

namespace BEditorCore.Data.PropertyData {
    /// <summary>
    /// 
    /// </summary>
    [DataContract(Namespace = "")]
    public class SelectorProperty : PropertyElement, IEasingSetting {
        private int selectIndex;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="metadata"></param>
        public SelectorProperty(SelectorPropertyMetadata metadata) {
            Index = metadata.DefaultIndex;
            PropertyMetadata = metadata;
        }

        /// <summary>
        /// 
        /// </summary>
        public object SelectItem => (PropertyMetadata as SelectorPropertyMetadata).ItemSource[Index];

        /// <summary>
        /// 
        /// </summary>
        [DataMember]
        public int Index { get => selectIndex; set => SetValue(value, ref selectIndex, nameof(Index)); }


        public static implicit operator int(SelectorProperty selector) => selector.Index;
        public override string ToString() => $"(Index:{Index} Item:{SelectItem} Name:{PropertyMetadata?.Name})";


        #region Commands

        /// <summary>
        /// 
        /// </summary>
        public class ChangeSelect : IUndoRedoCommand {
            private readonly SelectorProperty Selector;
            private readonly int select;
            private readonly int oldselect;

            /// <summary>
            /// 
            /// </summary>
            /// <param name="combo"></param>
            /// <param name="select"></param>
            public ChangeSelect(SelectorProperty combo, int select) {
                Selector = combo;
                this.select = select;
                oldselect = combo.Index;
            }

            /// <inheritdoc/>
            public void Do() => Selector.Index = select;

            /// <inheritdoc/>
            public void Redo() => Do();

            /// <inheritdoc/>
            public void Undo() => Selector.Index = oldselect;
        }

        #endregion
    }

    /// <summary>
    /// 
    /// </summary>
    public class SelectorPropertyMetadata : PropertyElementMetadata {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="name"></param>
        /// <param name="itemsource"></param>
        /// <param name="memberpath"></param>
        public SelectorPropertyMetadata(string name, IList itemsource, string memberpath = "") : base(name) {
            DefaultIndex = 0;
            ItemSource = itemsource;
            MemberPath = memberpath;
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="name"></param>
        /// <param name="index"></param>
        /// <param name="itemsource"></param>
        /// <param name="memberpath"></param>
        public SelectorPropertyMetadata(string name, int index, IList itemsource, string memberpath = "") : base(name) {
            DefaultIndex = index;
            ItemSource = itemsource;
            MemberPath = memberpath;
        }


        /// <summary>
        /// 
        /// </summary>
        public IList ItemSource { get; protected set; }
        /// <summary>
        /// 
        /// </summary>
        public int DefaultIndex { get; protected set; }
        /// <summary>
        /// 
        /// </summary>
        public string MemberPath { get; protected set; }
    }
}
