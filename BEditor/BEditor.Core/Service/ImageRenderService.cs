using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Media;
using BEditor.Core.Native;

namespace BEditor.Core.Service
{
    public abstract class ImageRenderService : IImageRenderService
    {
        [return: MaybeNull()]
        public virtual Image Ellipse(int width, int height, int line, Color color)
        {
            var result = ImageProcess.Ellipse(width, height, line, color.R, color.G, color.B, out var ptr);

            if (result != null) throw new Exception(result);

            return new Image(ptr);
        }
        [return: MaybeNull()]
        public virtual Image Rectangle(int width, int height, int line, Color color)
        {
            return null;
        }
        [return: MaybeNull()]
        public abstract Image Text(int size, Color color, string text, FontRecord font, string style, bool rightToLeft);
	}

    public interface IImageRenderService
    {
        [return: MaybeNull()]
        public Image Ellipse(int width, int height, int line, Color color);
        [return: MaybeNull()]
        public Image Rectangle(int width, int height, int line, Color color);
        [return: MaybeNull()]
        public Image Text(int size, Color color, string text, FontRecord font, string style, bool rightToLeft);
    }
}
