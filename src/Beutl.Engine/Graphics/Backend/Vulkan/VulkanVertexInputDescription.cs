using Silk.NET.Vulkan;

namespace Beutl.Graphics.Backend.Vulkan;

internal struct VulkanVertexInputDescription
{
    public VertexInputBindingDescription[] Bindings;
    public VertexInputAttributeDescription[] Attributes;
}
