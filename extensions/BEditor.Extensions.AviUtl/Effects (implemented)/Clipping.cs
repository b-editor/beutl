using System.Collections.Generic;

using BEditor.Drawing;
using BEditor.Drawing.Pixel;

namespace BEditor.Extensions.AviUtl.Effects
{
    public sealed class Clipping : IMappedEffect
    {
        public string Name => "クリッピング";

        public void Apply(ref Image<BGRA32> image, ObjectTable table, Dictionary<string, object> @params)
        {
            var top = @params.GetArgValue("上", 0);
            var bottom = @params.GetArgValue("下", 0);
            var left = @params.GetArgValue("左", 0);
            var right = @params.GetArgValue("右", 0);

            if (@params.GetArgValue("中心の位置を変更", true))
            {
                table.ox += -(right / 2) + (left / 2);
                table.oy += -(top / 2) + (bottom / 2);
            }

            if (image.Width <= left + right || image.Height <= top + bottom)
            {
                image.Dispose();
                image = new(1, 1, default(BGRA32));
                return;
            }

            var width = image.Width - left - right;
            var height = image.Height - top - bottom;
            var x = left;
            var y = top;

            var img1 = image[new Rectangle(x, y, width, height)];
            image.Dispose();

            image = img1;
        }
    }
}
