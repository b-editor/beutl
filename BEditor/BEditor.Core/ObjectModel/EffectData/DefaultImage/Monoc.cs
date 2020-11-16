using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditor.Core;
using BEditor.ObjectModel.ObjectData;
using BEditor.ObjectModel.ProjectData;
using BEditor.ObjectModel.PropertyData;
using BEditor.Media;
using BEditor.Properties;

namespace BEditor.ObjectModel.EffectData
{
    [DataContract(Namespace = "")]
    public class Monoc : ImageEffect
    {
        public static readonly ColorPropertyMetadata ColorMetadata = new(Resources.Color, 255, 255, 255);

        public Monoc()
        {
            Color = new(ColorMetadata);
        }


        #region EffectProperty
        public override string Name => Resources.Monoc;

        public override void Render(ref Image source, EffectRenderArgs args) => source.SetColor(Color);


        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            Color
        };

        #endregion



        [DataMember(Order = 0)]
        [PropertyMetadata(nameof(ColorMetadata), typeof(Monoc))]
        public ColorProperty Color { get; private set; }
    }
}
