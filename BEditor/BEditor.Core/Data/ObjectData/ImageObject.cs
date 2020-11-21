using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditor.Core.Data.EffectData;
using BEditor.Core.Data.ProjectData;
using BEditor.Core.Data.PropertyData;
using BEditor.Core.Data.PropertyData.Default;
using BEditor.Core.Extensions;
using BEditor.Core.Media;
using BEditor.Core.Properties;

using static BEditor.Core.Data.ObjectData.DefaultData;

using Image = BEditor.Core.Media.Image;

namespace BEditor.Core.Data.ObjectData
{
    [DataContract(Namespace = "")]
    public abstract class ImageObject : ObjectElement
    {
        public static readonly PropertyElementMetadata CoordinateMetadata = new(Resources.Coordinate);
        public static readonly PropertyElementMetadata ZoomMetadata = new(Resources.Zoom);
        public static readonly PropertyElementMetadata BlendMetadata = new(Resources.Blend);
        public static readonly PropertyElementMetadata AngleMetadata = new(Resources.Angle);
        public static readonly PropertyElementMetadata MaterialMetadata = new(Resources.Material);


        #region ObjectElement

        public override string Name => Resources.TypeOfDraw;

        public override void Render(EffectRenderArgs args)
        {
            Image base_img = OnRender(args);

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


            Parent.Parent.GraphicsContext.DrawImage(base_img, Parent, args.Frame);
            if (!(base_img?.IsDisposed ?? true))
            {
                base_img.Dispose();
            }

            Coordinate.ResetOptional();
        }

        public override void PropertyLoaded()
        {
            Coordinate.ExecuteLoaded(CoordinateMetadata);
            Zoom.ExecuteLoaded(ZoomMetadata);
            Blend.ExecuteLoaded(BlendMetadata);
            Angle.ExecuteLoaded(AngleMetadata);
            Material.ExecuteLoaded(MaterialMetadata);
        }

        #endregion

        public abstract Image OnRender(EffectRenderArgs args);

        public ImageObject()
        {
            Coordinate = new(CoordinateMetadata);
            Zoom = new(ZoomMetadata);
            Blend = new(BlendMetadata);
            Angle = new(AngleMetadata);
            Material = new(MaterialMetadata);
        }

        [DataMember(Order = 0)]
        public Coordinate Coordinate { get; private set; }

        [DataMember(Order = 1)]
        public Zoom Zoom { get; private set; }

        [DataMember(Order = 2)]
        public Blend Blend { get; private set; }

        [DataMember(Order = 3)]
        public Angle Angle { get; private set; }

        [DataMember(Order = 4)]
        public Material Material { get; private set; }
    }
}
