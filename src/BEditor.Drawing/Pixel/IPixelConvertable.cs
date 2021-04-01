namespace BEditor.Drawing.Pixel
{
    public interface IPixelConvertable<T> where T : unmanaged, IPixel<T>
    {
        public void ConvertTo(out T dst);

        public void ConvertFrom(T src);
    }
}
