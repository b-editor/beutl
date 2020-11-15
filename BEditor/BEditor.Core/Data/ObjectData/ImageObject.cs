using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditor.Core.Data.EffectData;
using BEditor.Core.Data.ProjectData;
using BEditor.Core.Data.PropertyData;
using BEditor.Core.Data.PropertyData.Default;
using BEditor.Core.Media;
using BEditor.Core.Properties;

using static BEditor.Core.Data.ObjectData.DefaultData;

using Image = BEditor.Core.Media.Image;

namespace BEditor.Core.Data.ObjectData
{
    [DataContract(Namespace = "")]
    public class ImageObject : ObjectElement
    {
        public static readonly PropertyElementMetadata CoordinateMetadata = new(Resources.Coordinate);
        public static readonly PropertyElementMetadata ZoomMetadata = new(Resources.Zoom);
        public static readonly PropertyElementMetadata BlendMetadata = new(Resources.Blend);
        public static readonly PropertyElementMetadata AngleMetadata = new(Resources.Angle);
        public static readonly PropertyElementMetadata MaterialMetadata = new(Resources.Material);


        #region ObjectElement

        public override string Name => Resources.TypeOfDraw;

        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            Coordinate,
            Zoom,
            Blend,
            Angle,
            Material,
            Custom
        };

        #endregion

        #region Rendering

        public override void Render(EffectRenderArgs args)
        {
            Image base_img = Custom.Render(args);

            if (base_img == null)
            {
                Coordinate.ResetOptional();
                return;
            }

            for (int i = 1; i < args.Schedules.Count; i++)
            {
                var effect = args.Schedules[i];

                if (effect is ImageEffect imageEffect)
                {
                    imageEffect.Render(ref base_img, args);
                }
                effect.Render(args);


                if (args.Handled)
                {
                    Coordinate.ResetOptional();
                    return;
                }
            }


            Parent.Parent.RenderingContext.DrawImage(base_img, Parent, args.Frame);
            if (!(base_img?.IsDisposed ?? true))
            {
                base_img.Dispose();
            }

            Coordinate.ResetOptional();
        }

        #endregion

        public ImageObject()
        {
            Coordinate = new(CoordinateMetadata);
            Zoom = new(ZoomMetadata);
            Blend = new(BlendMetadata);
            Angle = new(AngleMetadata);
            Material = new(MaterialMetadata);
        }

        //TODO : .NET5のソースジェネレーターを使う

        [DataMember(Order = 0)]
        [PropertyMetadata(nameof(CoordinateMetadata), typeof(ImageObject))]
        public Coordinate Coordinate { get; private set; }

        [DataMember(Order = 1)]
        [PropertyMetadata(nameof(ZoomMetadata), typeof(ImageObject))]
        public Zoom Zoom { get; private set; }

        [DataMember(Order = 2)]
        [PropertyMetadata(nameof(BlendMetadata), typeof(ImageObject))]
        public Blend Blend { get; private set; }

        [DataMember(Order = 3)]
        [PropertyMetadata(nameof(AngleMetadata), typeof(ImageObject))]
        public Angle Angle { get; private set; }

        [DataMember(Order = 4)]
        [PropertyMetadata(nameof(MaterialMetadata), typeof(ImageObject))]
        public Material Material { get; private set; }

        [DataMember(Order = 5)]
        public DefaultImageObject Custom { get; internal set; }
    }
}
