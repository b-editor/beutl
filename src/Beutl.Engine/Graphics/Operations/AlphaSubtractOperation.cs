using Beutl.Media.Pixel;

namespace Beutl.Graphics.Operations;

public readonly unsafe struct AlphaSubtractOperation(Bgra8888* data, Bgra8888* mask)
{
    public readonly void Invoke(int pos)
    {
        data[pos].A = (byte)(data[pos].A & mask[pos].A);
    }
}
