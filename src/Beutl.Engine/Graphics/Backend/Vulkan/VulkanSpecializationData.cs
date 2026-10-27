using System.Collections.Immutable;
using Silk.NET.Vulkan;

namespace Beutl.Graphics.Backend.Vulkan;

// One packed value buffer, with separate ID maps for every shader stage.
internal sealed class VulkanSpecializationData
{
    private readonly Dictionary<ShaderStage, (int Offset, int Count)> _ranges = [];

    public SpecializationMapEntry[] MapEntries { get; }
    public byte[] Data { get; }

    public VulkanSpecializationData(ImmutableArray<SpecializationConstant> constants)
    {
        if (constants.IsDefault) constants = [];
        Data = new byte[constants.Sum(c => c.SizeInBytes)];
        var offsets = new int[constants.Length];
        int offset = 0;
        for (int i = 0; i < constants.Length; i++)
        {
            offsets[i] = offset;
            constants[i].CopyValueTo(Data.AsSpan(offset));
            offset += constants[i].SizeInBytes;
        }

        var entries = new List<SpecializationMapEntry>();
        foreach (ShaderStage stage in new[] { ShaderStage.Vertex, ShaderStage.Fragment, ShaderStage.Compute,
                     ShaderStage.Geometry, ShaderStage.TessellationControl, ShaderStage.TessellationEvaluation })
        {
            int start = entries.Count;
            for (int i = 0; i < constants.Length; i++)
            {
                SpecializationConstant constant = constants[i];
                if ((constant.Stages & stage) == 0) continue;
                entries.Add(new SpecializationMapEntry
                {
                    ConstantID = constant.ConstantId,
                    Offset = (uint)offsets[i],
                    Size = (nuint)constant.SizeInBytes,
                });
            }
            _ranges.Add(stage, (start, entries.Count - start));
        }
        MapEntries = [.. entries];
    }

    // The caller keeps both arrays pinned until pipeline creation returns.
    public unsafe SpecializationInfo Describe(ShaderStage stage, SpecializationMapEntry* entries, byte* data)
    {
        var (offset, count) = _ranges[stage];
        return new SpecializationInfo
        {
            MapEntryCount = (uint)count,
            PMapEntries = count == 0 ? null : entries + offset,
            DataSize = (nuint)Data.Length,
            PData = data,
        };
    }
}
