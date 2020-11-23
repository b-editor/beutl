using System;
using System.Collections.Generic;
using System.IO;
using System.Reactive.Linq;
using System.Runtime.Serialization;

using BEditor.Core.Command;
using BEditor.Core.Data.Primitive.Properties;
using BEditor.Core.Data.Property;
using BEditor.Core.Properties;

namespace BEditor.Core.Data.Primitive.Objects.PrimitiveImages
{
    [DataContract(Namespace = "")]
    public class Image : ImageObject
    {
        public static readonly FilePropertyMetadata FileMetadata = new(Resources.File, "", "png,jpeg,jpg,bmp", Resources.ImageFile);
        private Media.Image source;

        public Image() => File = new(FileMetadata);


        #region Properties
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            Coordinate,
            Zoom,
            Blend,
            Angle,
            Material,
            File
        };


        [DataMember(Order = 0)]
        public FileProperty File { get; private set; }

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

        #endregion


        public override Media.Image OnRender(EffectRenderArgs args) => Source?.Clone();

        public override void PropertyLoaded()
        {
            base.PropertyLoaded();
            File.ExecuteLoaded(FileMetadata);

            File.Subscribe(filename =>
            {
                if (System.IO.File.Exists(filename))
                {
                    var file = new FileStream(File.File, FileMode.Open);
                    source = new(file, Media.ImageReadMode.UnChanged);
                }
            });
        }
    }
}
