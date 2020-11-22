using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.Serialization;

using BEditor.Core.Command;
using BEditor.Core.Data.Primitive.Properties;
using BEditor.Core.Data.Property;
using BEditor.Core.Extensions;

namespace BEditor.Core.Data.Primitive.Objects.PrimitiveImages
{
    [DataContract(Namespace = "")]
    public class Image : ImageObject
    {
        public static readonly FilePropertyMetadata FileMetadata = new(Core.Properties.Resources.File, "", "png,jpeg,jpg,bmp", Core.Properties.Resources.ImageFile);

        private Media.Image source;

        public Image() => File = new(FileMetadata);


        #region ImageObject
        public override Media.Image OnRender(EffectRenderArgs args) => Source?.Clone();

        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            Coordinate,
            Zoom,
            Blend,
            Angle,
            Material,
            File
        };

        public override void PropertyLoaded()
        {
            base.PropertyLoaded();
            File.ExecuteLoaded(FileMetadata);

            File.PropertyChanged += PathChanged;
        }

        #endregion


        [DataMember(Order = 0)]
        public FileProperty File { get; private set; }

        #region PathChanged
        private void PathChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(FileProperty.File))
            {
                return;
            }

            if (System.IO.File.Exists(File.File))
            {
                FileStream file = new FileStream(File.File, FileMode.Open);
                source = new Media.Image(file, Media.ImageReadMode.UnChanged);
            }
        }
        #endregion

        public Media.Image Source
        {
            get
            {
                if (source == null && System.IO.File.Exists(File.File))
                {
                    var file = new FileStream(File.File, FileMode.Open);
                    source = new(file, Media.ImageReadMode.UnChanged);
                }

                return source;
            }
            set => source = value;
        }
    }
}
