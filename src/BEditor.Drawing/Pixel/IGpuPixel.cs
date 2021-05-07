namespace BEditor.Drawing.Pixel
{
    public interface IGpuPixel<T> where T : unmanaged, IPixel<T>
    {
        public string GetBlend();

        public string GetAdd();

        public string Subtract();
    }
}