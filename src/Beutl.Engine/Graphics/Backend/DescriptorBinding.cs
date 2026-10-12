namespace Beutl.Graphics.Backend;

public struct DescriptorBinding
{
    public uint Binding { get; set; }

    public DescriptorType Type { get; set; }

    public uint Count { get; set; }

    public ShaderStage Stages { get; set; }

    public DescriptorBinding(uint binding, DescriptorType type, uint count, ShaderStage stages)
    {
        Binding = binding;
        Type = type;
        Count = count;
        Stages = stages;
    }
}
