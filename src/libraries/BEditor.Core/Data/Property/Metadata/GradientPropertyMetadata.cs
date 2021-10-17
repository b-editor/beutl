using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Drawing;

namespace BEditor.Data.Property
{
    public record GradientPropertyMetadata(string Name, Color Color1, Color Color2)
        : PropertyElementMetadata(Name), IEditingPropertyInitializer<GradientProperty>
    {
        public GradientProperty Create()
        {
            return new(this);
        }
    }
}
