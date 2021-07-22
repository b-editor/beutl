using System.Collections.Generic;

using BEditor.Drawing;
using BEditor.Drawing.Pixel;

namespace BEditor.Extensions.AviUtl
{
    public interface IMappedEffect
    {
        public string Name { get; }

        public void Apply(ref Image<BGRA32> image, ObjectTable table, Dictionary<string, object> @params);
    }
}
