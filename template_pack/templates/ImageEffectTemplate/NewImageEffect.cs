using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditor.Data;
using BEditor.Data.Primitive;
using BEditor.Data.Property;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;

namespace BEditorPluginTemplate
{
    [DataContract]
    public class NewImageEffect : ImageEffect
    {
        public NewImageEffect()
        {

        }

        public override string Name => "Effect name";
        public override IEnumerable<PropertyElement> Properties => Array.Empty<PropertyElement>();

        public override void Render(EffectRenderArgs<Image<BGRA32>> args)
        {

        }

        protected override void OnLoad()
        {
            base.OnLoad();
        }

        protected override void OnUnload()
        {
            base.OnUnload();
        }
    }
}