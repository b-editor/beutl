namespace Beutl.Graphics.Backend;

/// <summary>
/// Describes an immutable scalar value used to specialize one or more shader stages when a pipeline is created.
/// </summary>
/// <remarks>
/// The constant ID must match a SPIR-V specialization constant declared by every stage in <see cref="Stages"/>.
/// Specialization values are part of pipeline identity and cannot be changed after pipeline creation.
/// </remarks>
public readonly record struct SpecializationConstant
{
    private readonly ulong _valueBits;
    private readonly byte _sizeInBytes;

    private SpecializationConstant(
        uint constantId,
        ShaderStage stages,
        ulong valueBits,
        byte sizeInBytes)
    {
        ConstantId = constantId;
        Stages = stages;
        _valueBits = valueBits;
        _sizeInBytes = sizeInBytes;
    }

    /// <summary>
    /// Gets the SPIR-V specialization constant ID.
    /// </summary>
    public uint ConstantId { get; }

    /// <summary>
    /// Gets the shader stages specialized with this value.
    /// </summary>
    public ShaderStage Stages { get; }

    /// <summary>
    /// Gets the size of the scalar value in bytes.
    /// </summary>
    public int SizeInBytes => _sizeInBytes;

    /// <summary>
    /// Creates a 32-bit Boolean specialization constant.
    /// </summary>
    public static SpecializationConstant Create(uint constantId, bool value, ShaderStage stages)
        => new(constantId, stages, value ? 1u : 0u, sizeof(uint));

    /// <summary>
    /// Creates a signed 32-bit integer specialization constant.
    /// </summary>
    public static SpecializationConstant Create(uint constantId, int value, ShaderStage stages)
        => new(constantId, stages, unchecked((uint)value), sizeof(int));

    /// <summary>
    /// Creates an unsigned 32-bit integer specialization constant.
    /// </summary>
    public static SpecializationConstant Create(uint constantId, uint value, ShaderStage stages)
        => new(constantId, stages, value, sizeof(uint));

    /// <summary>
    /// Creates a 32-bit floating-point specialization constant.
    /// </summary>
    public static SpecializationConstant Create(uint constantId, float value, ShaderStage stages)
        => new(constantId, stages, BitConverter.SingleToUInt32Bits(value), sizeof(float));

    /// <summary>
    /// Creates a signed 64-bit integer specialization constant.
    /// </summary>
    public static SpecializationConstant Create(uint constantId, long value, ShaderStage stages)
        => new(constantId, stages, unchecked((ulong)value), sizeof(long));

    /// <summary>
    /// Creates an unsigned 64-bit integer specialization constant.
    /// </summary>
    public static SpecializationConstant Create(uint constantId, ulong value, ShaderStage stages)
        => new(constantId, stages, value, sizeof(ulong));

    /// <summary>
    /// Creates a 64-bit floating-point specialization constant.
    /// </summary>
    public static SpecializationConstant Create(uint constantId, double value, ShaderStage stages)
        => new(constantId, stages, unchecked((ulong)BitConverter.DoubleToInt64Bits(value)), sizeof(double));

    /// <summary>
    /// Copies the immutable scalar value to the start of <paramref name="destination"/> in its native binary
    /// representation. Exactly <see cref="SizeInBytes"/> bytes are written.
    /// </summary>
    /// <param name="destination">The destination buffer, which must be at least <see cref="SizeInBytes"/> bytes.</param>
    public void CopyValueTo(Span<byte> destination)
    {
        if (destination.Length < _sizeInBytes)
        {
            throw new ArgumentException("The destination is too small for the specialization value.", nameof(destination));
        }

        if (_sizeInBytes == sizeof(uint))
        {
            BitConverter.TryWriteBytes(destination, unchecked((uint)_valueBits));
        }
        else if (_sizeInBytes == sizeof(ulong))
        {
            BitConverter.TryWriteBytes(destination, _valueBits);
        }
        else
        {
            throw new InvalidOperationException("The specialization constant has an invalid scalar size.");
        }
    }
}
