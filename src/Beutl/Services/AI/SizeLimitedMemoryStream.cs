namespace Beutl.Services.AI;

internal sealed class SizeLimitedMemoryStream(int maximumBytes) : MemoryStream
{
    public override void Write(byte[] buffer, int offset, int count)
    {
        EnsureWithinLimit(count);
        base.Write(buffer, offset, count);
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        EnsureWithinLimit(buffer.Length);
        base.Write(buffer);
    }

    public override Task WriteAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        EnsureWithinLimit(count);
        return base.WriteAsync(buffer, offset, count, cancellationToken);
    }

    public override ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        EnsureWithinLimit(buffer.Length);
        return base.WriteAsync(buffer, cancellationToken);
    }

    public override void WriteByte(byte value)
    {
        EnsureWithinLimit(1);
        base.WriteByte(value);
    }

    private void EnsureWithinLimit(int additionalBytes)
    {
        if (maximumBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        if (additionalBytes < 0 || Position > maximumBytes - additionalBytes)
            throw new InvalidDataException("The AI result exceeds the supported size.");
    }
}
