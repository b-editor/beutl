using System.Runtime.CompilerServices;

namespace Beutl.Graphics.Effects;

[InlineArray(ByteSize)]
internal struct SpirvPushConstants
{
    public const int ByteSize = 128;
    public const int UserByteOffset = 16;

    private byte _element0;
}
