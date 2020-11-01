using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.Serialization;

using BEditor.Core.Data.ProjectData;
using BEditor.Core.Data.PropertyData;

namespace BEditor.Core.Data.ObjectData {
    public partial class DefaultData {
        [DataContract(Namespace = "")]
        public class Image : DefaultImageObject {
            public static readonly FilePropertyMetadata FileMetadata = new FilePropertyMetadata(Properties.Resources.File, "", "png,jpeg,jpg,bmp", Properties.Resources.ImageFile);

            private Media.Image source;

            public Image() => File = new FileProperty(FileMetadata);


            #region DefaultImageObjectメンバー
            public override Media.Image Load(EffectLoadArgs args) => Source?.Clone();

            public override IList<PropertyElement> GroupItems => new List<PropertyElement>() {
                File
            };

            public override void PropertyLoaded() {
                base.PropertyLoaded();

                File.PropertyChanged += PathChanged;
            }

            #endregion


            [DataMember(Order = 0)]
            [PropertyMetadata(nameof(FileMetadata), typeof(Image))]
            public FileProperty File { get; set; }

            #region PathChanged
            private void PathChanged(object sender, PropertyChangedEventArgs e) {
                if (e.PropertyName != nameof(FileProperty.File)) {
                    return;
                }

                if (System.IO.File.Exists(File.File)) {
                    source = new Media.Image(File.File);
                }
            }
            #endregion

            public Media.Image Source {
                get {
                    if (source == null && System.IO.File.Exists(File.File)) {
                        source = new Media.Image(File.File);
                    }

                    return source;
                }
                set => source = value;
            }
        }
    }
}
