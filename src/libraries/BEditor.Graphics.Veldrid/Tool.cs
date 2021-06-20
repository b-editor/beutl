using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Drawing;

using Veldrid;

namespace BEditor.Graphics.Veldrid
{
    internal static class Tool
    {
        public static RgbaFloat ToFloat(this Color color)
        {
            return new(
                color.R / 255f,
                color.G / 255f,
                color.B / 255f,
                color.A / 255f);
        }

        public static void Dispose<T>(this IEnumerable<T> disposables)
            where T : IDisposable
        {
            foreach (var item in disposables)
            {
                item.Dispose();
            }
        }

        public static void Dispose<T>(this T[] disposables)
            where T : IDisposable
        {
            foreach (var item in disposables)
            {
                item.Dispose();
            }
        }
    }
}
