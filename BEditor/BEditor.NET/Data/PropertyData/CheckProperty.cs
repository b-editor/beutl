using System;
using System.Runtime.Serialization;

using BEditor.NET.Data.PropertyData.EasingSetting;

namespace BEditor.NET.Data.PropertyData {
    /// <summary>
    /// 
    /// </summary>
    [DataContract(Namespace = "")]
    public class CheckProperty : PropertyElement, IEasingSetting {
        private bool isChecked;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="metadata"></param>
        public CheckProperty(CheckPropertyMetadata metadata) {
            PropertyMetadata = metadata;
            isChecked = metadata.DefaultIsChecked;
        }

        /// <summary>
        /// 
        /// </summary>
        [DataMember]
        public bool IsChecked { get => isChecked; set => SetValue(value, ref isChecked, nameof(IsChecked)); }

        /// <summary>
        /// 
        /// </summary>
        public class ChangeChecked : IUndoRedoCommand {
            private readonly CheckProperty CheckSetting;
            private readonly bool value;

            /// <summary>
            /// 
            /// </summary>
            /// <param name="property"></param>
            /// <param name="value"></param>
            public ChangeChecked(CheckProperty property, bool value) {
                CheckSetting = property;
                this.value = value;
            }

            /// <summary>
            /// 
            /// </summary>
            public void Do() => CheckSetting.IsChecked = value;

            /// <summary>
            /// 
            /// </summary>
            public void Redo() => Do();

            /// <summary>
            /// 
            /// </summary>
            public void Undo() => CheckSetting.IsChecked = !value;
        }
    }

    /// <summary>
    /// 
    /// </summary>
    public class CheckPropertyMetadata : PropertyElementMetadata {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="name"></param>
        public CheckPropertyMetadata(string name) : base(name) => DefaultIsChecked = false;
        /// <summary>
        /// 
        /// </summary>
        /// <param name="name"></param>
        /// <param name="defaultvalue"></param>
        public CheckPropertyMetadata(string name, bool defaultvalue) : base(name) => DefaultIsChecked = defaultvalue;

        /// <summary>
        /// 
        /// </summary>
        public bool DefaultIsChecked { get; private set; }
    }
}
