using System;
using System.Collections.Generic;

using BEditor.Drawing;
using BEditor.Drawing.Pixel;

namespace BEditor.Extensions.AviUtl.Effects
{
    public sealed class Fade : IMappedEffect
    {
        public string Name => "フェード";

        public void Apply(ref Image<BGRA32> image, ObjectTable table, Dictionary<string, object> @params)
        {
            throw new NotImplementedException();
        }
    }
}