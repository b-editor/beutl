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
    public class NewImageObject : ImageObject
    {
        public NewImageObject()
        {

        }

        public override string Name => "Object name";
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            Coordinate,
            Zoom,
            Blend,
            Angle,
            Material
        };

        protected override Image<BGRA32>? Render(EffectRenderArgs args)
        {
            return null;
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