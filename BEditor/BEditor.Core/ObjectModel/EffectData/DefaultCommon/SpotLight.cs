using System;
using System.Collections.Generic;
using System.Text;

using BEditor.ObjectModel.ProjectData;
using BEditor.ObjectModel.PropertyData;
namespace BEditor.ObjectModel.EffectData.DefaultCommon
{
    public class SpotLight : EffectElement
    {
        public override string Name => throw new NotImplementedException();
        public override IEnumerable<PropertyElement> Properties => throw new NotImplementedException();
        public override void Render(EffectRenderArgs args) => throw new NotImplementedException();
    }
}
