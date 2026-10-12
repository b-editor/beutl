namespace Beutl.Graphics.Backend;

public struct DescriptorPoolSize
{
    public DescriptorType Type { get; set; }

    public uint Count { get; set; }

    public DescriptorPoolSize(DescriptorType type, uint count)
    {
        Type = type;
        Count = count;
    }
}
