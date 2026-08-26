using System;
using System.Linq;
using Silk.NET.Vulkan;
using VkDescriptorType = Silk.NET.Vulkan.DescriptorType;

namespace Beutl.Graphics.Backend.Vulkan;

/// <summary>
/// The descriptor bindings a pipeline layout declares, kept so a write against a set allocated from that
/// layout can be checked before it reaches <c>vkUpdateDescriptorSets</c>.
/// </summary>
/// <remarks>
/// A <see cref="DescriptorSetLayout"/> handle carries none of the declarations it was built from, and the
/// write struct names a binding and a descriptor type with nothing tying either back to the layout. So a
/// write to a binding the layout never declared, or one whose type disagrees with the declaration, is
/// undefined behaviour the driver need not diagnose - on MoltenVK it takes the process down. The managed
/// layer has to hold what the native layer cannot check.
/// </remarks>
internal sealed class VulkanDescriptorBindingTable
{
    /// <summary>
    /// The largest binding number the dense table covers. Above it the declarations are searched linearly,
    /// so an absurd binding number cannot turn into an absurd allocation.
    /// </summary>
    private const uint MaxDenseBinding = 1023;

    private readonly Declaration[] _declarations;
    private readonly Entry[] _dense;

    public VulkanDescriptorBindingTable(DescriptorSetLayoutBinding[] bindings)
    {
        _declarations = bindings
            .Select(binding => new Declaration(binding.Binding, binding.DescriptorType, binding.DescriptorCount))
            .ToArray();

        uint maxBinding = _declarations.Length == 0 ? 0 : _declarations.Max(d => d.Binding);
        if (_declarations.Length != 0 && maxBinding <= MaxDenseBinding)
        {
            _dense = new Entry[maxBinding + 1];
            foreach (Declaration declaration in _declarations)
            {
                _dense[declaration.Binding] = new Entry(true, declaration.Type, declaration.Count);
            }
        }
        else
        {
            _dense = [];
        }
    }

    /// <summary>
    /// Rejects a descriptor write the pipeline layout does not describe.
    /// </summary>
    /// <remarks>
    /// On the per-draw path, so the declared binding is reached by a bounds check and one array index
    /// whenever the layout's binding numbers are compact - which every pipeline in the tree is.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="binding"/> is negative.</exception>
    /// <exception cref="ArgumentException">
    /// The layout declares no such binding, declares it as a different descriptor type, or declares fewer
    /// descriptors than the write covers.
    /// </exception>
    public void ValidateWrite(
        int binding,
        VkDescriptorType descriptorType,
        uint arrayElement,
        uint descriptorCount,
        string parameterName)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(binding, parameterName);

        Entry entry = Find((uint)binding);
        if (!entry.IsDeclared)
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

        if (arrayElement >= entry.Count || descriptorCount > entry.Count - arrayElement)
        {
            throw new ArgumentException(
                $"Binding {binding} declares {entry.Count} descriptor(s), so writing {descriptorCount} at "
                + $"array element {arrayElement} runs past the declared array.",
                parameterName);
        }
    }

    private Entry Find(uint binding)
    {
        if (binding < (uint)_dense.Length)
            return _dense[binding];

        foreach (Declaration declaration in _declarations)
        {
            if (declaration.Binding == binding)
                return new Entry(true, declaration.Type, declaration.Count);
        }

        return default;
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

    private readonly record struct Entry(bool IsDeclared, VkDescriptorType Type, uint Count);
}
