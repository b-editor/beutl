using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BEditor.Core.Data.Control
{
    [AttributeUsage(AttributeTargets.Class)]
    public class CustomClipUIAttribute : Attribute
    {
        public CustomClipUIAttribute()
        {

        }

        public int Color { get; set; } = unchecked((int)0xff304fee);
        public Color GetColor => System.Drawing.Color.FromArgb(Color);
    }
}
