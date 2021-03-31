namespace BEditor.Drawing.Pixel
{
    public interface IPixel<T> where T : unmanaged, IPixel<T>
    {
        public T Blend(T foreground);
        public T Add(T foreground);
        public T Subtract(T foreground);
    }
}
