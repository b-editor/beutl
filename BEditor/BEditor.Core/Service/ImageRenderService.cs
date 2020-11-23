using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Exceptions;
using BEditor.Core.Media;
using BEditor.Core.Native;

namespace BEditor.Core.Service
{
    public class ImageRenderService : IImageRenderService
    {
        [return: MaybeNull()]
        public Image Ellipse(int width, int height, int line, Color color)
        {
            var result = ImageProcess.Ellipse(width, height, line, color.R, color.G, color.B, out var ptr);

            if (result != null) throw new Exception(result);

            return new Image(ptr);
        }
        [return: MaybeNull()]
        public Image Rectangle(int width, int height, int line, Color color)
        {
            return null;
        }
        [return: MaybeNull()]
        public Image Text(int size, Color color, string text, FontRecord font, string style, bool rightToLeft)
        {
            if (string.IsNullOrEmpty(text)) return null;
            if (font is null) return null;

            //intへ変換
            var styleint = style switch
            {
                "Normal" => FontStyle.Normal,
                "Bold" => FontStyle.Bold,
                "Italic" => FontStyle.Italic,
                "UnderLine" => FontStyle.UnderLine,
                "StrikeThrough" => FontStyle.StrikeThrough,
                _ => throw new NotImplementedException(),
            };
            var fontp = new Font(font.Path, size) { Style = styleint };

            var result = fontp.RenderText(text, color);
            fontp.Dispose();

            return result;
        }
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
