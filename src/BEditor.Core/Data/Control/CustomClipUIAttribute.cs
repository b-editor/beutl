using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Drawing;

namespace BEditor.Core.Data
{
    /// <summary>
    /// Represents an attribute that provides the ability to specify the color of the clip.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class CustomClipUIAttribute : Attribute
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="CustomClipUIAttribute"/> class.
        /// </summary>
        public CustomClipUIAttribute()
        {

        }

        /// <summary>
        /// Gets or sets the rgb color of int type.
        /// </summary>
        public int Color { get; set; } = unchecked(0x304fee);
        /// <summary>
        /// Get <see cref="Drawing.Color"/> from <see cref="Color"/>.
        /// </summary>
        public Color GetColor => BEditor.Drawing.Color.FromARGB(Color);
    }
}
