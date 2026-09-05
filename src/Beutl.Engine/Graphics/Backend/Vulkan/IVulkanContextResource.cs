namespace Beutl.Graphics.Backend.Vulkan;

/// <summary>A backend object whose Vulkan handles are only valid on the context that created them.</summary>
/// <remarks>
/// Vulkan handles carry no device provenance, so submitting one to a different device is undefined behaviour
/// the driver is not required to diagnose. Every backend entry point that accepts a caller-supplied resource
/// resolves it through <see cref="VulkanContext.RequireOwned{TResource}"/>, which uses this to reject a
/// resource that belongs to another context before its handle reaches a native call.
/// </remarks>
internal interface IVulkanContextResource
{
    VulkanContext OwnerContext { get; }
}
