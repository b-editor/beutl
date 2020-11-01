using System;
using System.Runtime.Serialization;

using BEditor.Core.Data.PropertyData.EasingSetting;

namespace BEditor.Core.Data.PropertyData {
    /// <summary>
    /// 
    /// </summary>
    [DataContract(Namespace = "")]
    public sealed class FileProperty : PropertyElement, IEasingSetting {
        private string file;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="file"></param>
        public FileProperty(FilePropertyMetadata file) {
            File = file.DefaultFile;
            PropertyMetadata = file;
        }

        /// <summary>
        /// 
        /// </summary>
        [DataMember]
        public string File { get => file; set => SetValue(value, ref file, nameof(File)); }

        public static implicit operator string(FileProperty property) => property.File;
        public override string ToString() => $"(File:{File} Name:{PropertyMetadata?.Name})";


        #region Commands

        /// <summary>
        /// 
        /// </summary>
        public sealed class ChangePath : IUndoRedoCommand {
            private readonly FileProperty FileSetting;
            private readonly string path;
            private readonly string oldpath;

            /// <summary>
            /// 
            /// </summary>
            /// <param name="property"></param>
            /// <param name="path"></param>
            public ChangePath(FileProperty property, string path) {
                FileSetting = property;
                this.path = path;
                oldpath = FileSetting.File;
            }


            /// <inheritdoc/>
            public void Do() => FileSetting.File = path;

            /// <inheritdoc/>
            public void Redo() => Do();

            /// <inheritdoc/>
            public void Undo() => FileSetting.File = oldpath;
        }

        #endregion
    }

    /// <summary>
    /// 
    /// </summary>
    public class FilePropertyMetadata : PropertyElementMetadata {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="name"></param>
        public FilePropertyMetadata(string name) : base(name) {

        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="name"></param>
        /// <param name="defaultfile"></param>
        /// <param name="defaultfilter"></param>
        /// <param name="defaultfiltername"></param>
        public FilePropertyMetadata(string name, string defaultfile, string defaultfilter, string defaultfiltername) : base(name) {
            DefaultFile = defaultfile;
            Filter = defaultfilter;
            FilterName = defaultfiltername;
        }

        /// <summary>
        /// 
        /// </summary>
        public string DefaultFile { get; private set; }
        /// <summary>
        /// 
        /// </summary>
        public string Filter { get; private set; }
        /// <summary>
        /// 
        /// </summary>
        public string FilterName { get; private set; }
    }
}
