namespace BEditor.Drawing.Pixel
{
    public interface IPixelConvertable<T> where T : unmanaged, IPixel<T>
    {
        public void Convert(out T dst);
    }
}
