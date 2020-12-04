using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditor.Core.Command;
using BEditor.Core.Data.Primitive.Objects;
using BEditor.Core.Data.Primitive.Properties;
using BEditor.Core.Data.Primitive.Properties.PrimitiveGroup;
using BEditor.Core.Data.Property;
using BEditor.Core.Extensions;
using BEditor.Core.Graphics;
using BEditor.Core.Media;
using BEditor.Core.Properties;
using BEditor.Core.Renderings;
using BEditor.Drawing;

using OpenTK.Graphics.OpenGL;

#if OldOpenTK
using GLColor = OpenTK.Graphics.Color4;
#else
using GLColor = OpenTK.Mathematics.Color4;
#endif

namespace BEditor.Core.Data.Primitive.Effects.PrimitiveImages
{
    public class MultiLayerization : ImageEffect
    {
        public static readonly EasePropertyMetadata ZMetadata = new(Resources.Z, 50, float.NaN, 0);
        public static readonly ColorAnimationPropertyMetadata ColorMetadata = new(Resources.Color, 255, 255, 255, 255, true);


        public MultiLayerization()
        {
            Z = new(ZMetadata);
            Material = new(ImageObject.MaterialMetadata);
            Color = new(ColorMetadata);
        }


        #region Properties

        public override string Name => Resources.MultiLayerization;

        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            Z,
            Color
        };


        [DataMember(Order = 0)]
        public EaseProperty Z { get; private set; }

        [DataMember(Order = 1)]
        public Material Material { get; private set; }

        [DataMember(Order = 2)]
        public ColorAnimationProperty Color { get; private set; }

        #endregion


        public override void PreviewRender(EffectRenderArgs args)
        {
            base.PreviewRender(args);

            args.Schedules.Remove(this);
            args.Schedules.Add(this);
        }
        public override void Render(EffectRenderArgs<Image<BGRA32>> args)
        {
        }
        public override void PropertyLoaded()
        {
            Z.ExecuteLoaded(ZMetadata);
            Material.ExecuteLoaded(ImageObject.MaterialMetadata);
            Color.ExecuteLoaded(ColorMetadata);
        }
    }
}
