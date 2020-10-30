using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditor.NET.Data.EffectData;
using BEditor.NET.Data.ProjectData;
using BEditor.NET.Data.PropertyData;
using BEditor.NET.Data.PropertyData.Default;
using BEditor.NET.Media;
using BEditor.NET.Properties;

using static BEditor.NET.Data.ObjectData.DefaultData;

using Image = BEditor.NET.Media.Image;

namespace BEditor.NET.Data.ObjectData {
    [DataContract(Namespace = "")]
    public class ImageObject : ObjectElement {
        public static readonly PropertyElementMetadata CoordinateMetadata = new PropertyElementMetadata(Resources.Coordinate);
        public static readonly PropertyElementMetadata ZoomMetadata = new PropertyElementMetadata(Resources.Zoom);
        public static readonly PropertyElementMetadata BlendMetadata = new PropertyElementMetadata(Resources.Blend);
        public static readonly PropertyElementMetadata AngleMetadata = new PropertyElementMetadata(Resources.Angle);
        public static readonly PropertyElementMetadata MaterialMetadata = new PropertyElementMetadata(Resources.Material);


        #region ObjectElement

        public override string Name => Resources.TypeOfDraw;

        public override IList<PropertyElement> PropertySettings => new List<PropertyElement>() {
            Coordinate,
            Zoom,
            Blend,
            Angle,
            Material,
            Custom
        };

        #endregion

        #region Rendering

        public override void Load(EffectLoadArgs args) {
            Image base_img = Custom.Load(args);

            if (base_img == null) {
                Coordinate.ResetOptional();
                return;
            }

            for (int i = 1; i < args.Schedules.Count; i++) {
                var effect = args.Schedules[i];

                if (effect is ImageEffect imageEffect) {
                    imageEffect.Draw(ref base_img, args);
                }
                effect.Load(args);


                if (args.Handled) {
                    Coordinate.ResetOptional();
                    return;
                }
            }


            ClipData.Scene.RenderingContext.DrawImage(base_img, ClipData, args.Frame);
            if (!(base_img?.IsDisposed ?? true)) {
                base_img.Dispose();
            }

            Coordinate.ResetOptional();
        }

        #endregion

        public ImageObject() {
            Coordinate = new Coordinate(CoordinateMetadata);
            Zoom = new Zoom(ZoomMetadata);
            Blend = new Blend(BlendMetadata);
            Angle = new Angle(AngleMetadata);
            Material = new Material(MaterialMetadata);
        }



        [DataMember(Order = 0)]
        [PropertyMetadata(nameof(CoordinateMetadata), typeof(ImageObject))]
        public Coordinate Coordinate { get; set; }

        [DataMember(Order = 1)]
        [PropertyMetadata(nameof(ZoomMetadata), typeof(ImageObject))]
        public Zoom Zoom { get; set; }

        [DataMember(Order = 2)]
        [PropertyMetadata(nameof(BlendMetadata), typeof(ImageObject))]
        public Blend Blend { get; set; }

        [DataMember(Order = 3)]
        [PropertyMetadata(nameof(AngleMetadata), typeof(ImageObject))]
        public Angle Angle { get; set; }

        [DataMember(Order = 4)]
        [PropertyMetadata(nameof(MaterialMetadata), typeof(ImageObject))]
        public Material Material { get; set; }

        [DataMember(Order = 5)]
        public DefaultImageObject Custom { get; set; }
    }
}
