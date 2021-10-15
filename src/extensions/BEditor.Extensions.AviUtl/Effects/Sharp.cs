using System;
using System.Collections.Generic;

using BEditor.Drawing;
using BEditor.Drawing.Pixel;

namespace BEditor.Extensions.AviUtl.Effects
{
    public sealed class Sharp : IMappedEffect
    {
        public string Name => "シャープ";

        public void Apply(ref Image<BGRA32> image, ObjectTable table, Dictionary<string, object> @params)
        {
            throw new NotImplementedException();
        }
    }
}