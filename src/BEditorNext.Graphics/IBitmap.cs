namespace BEditorNext.Graphics;

public interface IBitmap : IDisposable, ICloneable
{
    public int Width { get; }

    public int Height { get; }

    public int ByteCount { get; }

    public int PixelSize { get; }

    public IntPtr Data { get; }

    public bool IsDisposed { get; }
}
