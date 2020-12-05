using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Media;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;

namespace BEditor.Core.Service
{
    public interface IImageRenderService
    {
        [return: MaybeNull()]
        public Image<BGRA32> Ellipse(int width, int height, int line, Color color);
        [return: MaybeNull()]
        public Image<BGRA32> Rectangle(int width, int height, int line, Color color);
        [return: MaybeNull()]
        public Image<BGRA32> Text(int size, Color color, string text, FontRecord font, string style, bool rightToLeft);
    }
}
