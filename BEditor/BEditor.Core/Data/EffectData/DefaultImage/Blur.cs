using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditor.Core.Data.ProjectData;
using BEditor.Core.Data.PropertyData;
using BEditor.Core.Media;

namespace BEditor.Core.Data.EffectData
{
    [DataContract(Namespace = "")]
    public class Blur : ImageEffect
    {
        static readonly EasePropertyMetadata SizeMetadata = new EasePropertyMetadata(Properties.Resources.Size, 70, float.NaN, 0);
        static readonly CheckPropertyMetadata AlphaBlurMetadata = new CheckPropertyMetadata(Properties.Resources.Diffusion, false);
        static readonly SelectorPropertyMetadata ModeMetadata = new SelectorPropertyMetadata(Properties.Resources.BlurMode, new string[3]{
            Properties.Resources.Standard,
            Properties.Resources.Gauss,
            Properties.Resources.Median
        });


        public Blur()
        {
            Size = new EaseProperty(SizeMetadata);
            AlphaBlur = new CheckProperty(AlphaBlurMetadata);
            Mode = new SelectorProperty(ModeMetadata);
        }


        #region ImageEffect
        public override string Name => Properties.Resources.Blur;

        #region Draw
        public override void Draw(ref Image source, EffectRenderArgs args)
        {
            if (Mode.Index == 0)
            {
                source.Blur((int)Size.GetValue(args.Frame), AlphaBlur.IsChecked);
            }
            else if (Mode.Index == 1)
            {
                source.GaussianBlur((int)Size.GetValue(args.Frame), AlphaBlur.IsChecked);
            }
            else
            {
                source.MedianBlur((int)Size.GetValue(args.Frame), AlphaBlur.IsChecked);
            }
        }
        #endregion



        public override IList<PropertyElement> PropertySettings => new List<PropertyElement> {
            Size,
            AlphaBlur,
            Mode
        };

        #endregion


        [DataMember(Order = 0)]
        [PropertyMetadata(nameof(SizeMetadata), typeof(Blur))]
        public EaseProperty Size { get; set; }

        [DataMember(Order = 1)]
        [PropertyMetadata(nameof(AlphaBlurMetadata), typeof(Blur))]
        public CheckProperty AlphaBlur { get; set; }

        [DataMember(Order = 2)]
        [PropertyMetadata(nameof(ModeMetadata), typeof(Blur))]
        public SelectorProperty Mode { get; set; }
    }
}
