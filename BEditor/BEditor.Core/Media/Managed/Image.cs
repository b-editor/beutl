using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BEditor.Core.Media.Managed
{
    public unsafe class Image<T> where T : unmanaged, IPixel<T>
    {
        public Image(int width, int height)
        {
            Width = width;
            Height = height;
            Data = new T[width, height];
        }
        public Image(int width, int height, T[,] data)
        {
            Width = width;
            Height = height;
            Data = data;
        }
        public Image(int width, int height, T* data) : this(width, height)
        {

        }


        public int Width { get; }
        public int Height { get; }
        public T[,] Data { get; }
    }
}
