using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Drawing;

namespace BEditor.Core.Data.Control
{
    [AttributeUsage(AttributeTargets.Class)]
    public class CustomClipUIAttribute : Attribute
    {
        public CustomClipUIAttribute()
        {

        }

        public int Color { get; set; } = unchecked(0x304fee);
        public Color GetColor => BEditor.Drawing.Color.FromARGB(Color);
    }
}
