using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Data;
using BEditor.Data.Primitive;
using BEditor.Data.Property;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Primitive.Resources;

namespace BEditor.Primitive.Effects
{
    /// <summary>
    /// Represents an effect that reverses the opacity of an image.
    /// </summary>
    public class ReverseOpacity : ImageEffect
    {
        /// <inheritdoc/>
        public override string Name => Strings.ReverseOpacity;
        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> Properties => Array.Empty<PropertyElement>();

        /// <inheritdoc/>
        public override void Render(EffectRenderArgs<Image<BGRA32>> args)
        {
            var context = Parent.Parent.DrawingContext;

            if (context is not null && Settings.Default.PrioritizeGPU)
            {
                args.Value.ReverseOpacity(context);
            }
            else
            {
                args.Value.ReverseOpacity();
            }
        }
    }
}
