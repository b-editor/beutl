namespace BEditorNext.Graphics.Pixel;

public interface IPixel<T>
    where T : unmanaged, IPixel<T>
{
    public T FromColor(Color color);

    public Color ToColor();
}
