using System;
using System.Linq;
using Silk.NET.Vulkan;
using VkDescriptorType = Silk.NET.Vulkan.DescriptorType;

namespace Beutl.Graphics.Backend.Vulkan;

/// <summary>
/// The descriptor bindings a pipeline layout declares, kept so a write against a set allocated from that
/// layout can be checked before it reaches <c>vkUpdateDescriptorSets</c>.
/// </summary>
internal sealed class VulkanDescriptorBindingTable
{
    private readonly Declaration[] _declarations;

    public VulkanDescriptorBindingTable(DescriptorSetLayoutBinding[] bindings)
    {
        _declarations = bindings
            .Select(binding => new Declaration(binding.Binding, binding.DescriptorType, binding.DescriptorCount))
            .ToArray();
    }

    /// <summary>
    /// Rejects a write of one descriptor at array element 0 that the pipeline layout does not describe.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="binding"/> is negative.</exception>
    /// <exception cref="ArgumentException">
    /// The layout declares no such binding, declares it as a different descriptor type, or declares no
    /// descriptor for it.
    /// </exception>
    public void ValidateWrite(int binding, VkDescriptorType descriptorType, string parameterName)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(binding, parameterName);

        if (Find((uint)binding) is not { } entry)
        {
            throw new ArgumentException(
                $"Binding {binding} is not declared by the pipeline layout this descriptor set was allocated "
                + $"from. It declares {DescribeDeclarations()}.",
                parameterName);
        }

        if (entry.Type != descriptorType)
        {
            throw new ArgumentException(
                $"Binding {binding} is declared as {entry.Type} by the pipeline layout, but the write is a "
                + $"{descriptorType}.",
                parameterName);
        }

        if (entry.Count == 0)
        {
            throw new ArgumentException(
                $"Binding {binding} declares {entry.Count} descriptor(s), so writing 1 at "
                + "array element 0 runs past the declared array.",
                parameterName);
        }
    }

    private Declaration? Find(uint binding)
    {
        foreach (Declaration declaration in _declarations)
        {
            if (declaration.Binding == binding)
                return declaration;
        }

        return null;
    }

    private string DescribeDeclarations()
    {
        if (_declarations.Length == 0)
            return "no bindings";

        return string.Join(
            ", ",
            _declarations.Select(d => $"binding {d.Binding} as {d.Count}x {d.Type}"));
    }

    private readonly record struct Declaration(uint Binding, VkDescriptorType Type, uint Count);
}
