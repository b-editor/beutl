namespace Beutl.Graphics.Rendering.Requests;

internal readonly record struct RenderRequestId
{
    public RenderRequestId(long value)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "A render request ID must be positive.");
        }

        Value = value;
    }

    public long Value { get; }
}
