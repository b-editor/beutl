using Silk.NET.Vulkan;

namespace Beutl.Graphics.Backend.Vulkan;

/// <summary>
/// Vulkan-specific vertex input description.
/// </summary>
internal struct VulkanVertexInputDescription
{
    public VertexInputBindingDescription[] Bindings;
    public VertexInputAttributeDescription[] Attributes;
}
