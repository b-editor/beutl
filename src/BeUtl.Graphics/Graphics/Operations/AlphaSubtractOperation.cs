using BeUtl.Media.Pixel;

namespace BeUtl.Graphics.Operations;

public readonly unsafe struct AlphaSubtractOperation
{
    private readonly Bgra8888* _data;
    private readonly Bgra8888* _mask;

    public AlphaSubtractOperation(Bgra8888* data, Bgra8888* mask)
    {
        _data = data;
        _mask = mask;
    }

    public readonly void Invoke(int pos)
    {
        _data[pos].A = (byte)(_data[pos].A & _mask[pos].A);
    }
}
