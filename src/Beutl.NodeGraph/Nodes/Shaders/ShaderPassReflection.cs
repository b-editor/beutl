using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Beutl.Graphics;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Backend.Vulkan;
using Beutl.Graphics.Shaders;
using Beutl.Language;

namespace Beutl.NodeGraph.Nodes.Shaders;

/// <summary>What the stages of one pass declare together, read from the compiled code.</summary>
internal sealed record ShaderPassReflection(
    ImmutableArray<ShaderDescriptorLayout> Descriptors,
    string? PushConstantName,
    ImmutableArray<ShaderValueLayout> PushConstants,
    int PushConstantSize,
    ImmutableArray<ShaderColorOutputLayout> ColorOutputs,
    ImmutableArray<(ShaderSpecializationLayout Layout, ShaderStage Stages)> Specializations,
    ImmutableArray<(ShaderStage Stage, byte[] Spirv)> Stages)
{
    private const int MaximumCachedStages = 256;

    // A project's passes, and every frame of each, ask again for code that rarely changes.
    private static readonly ConcurrentDictionary<(ShaderStage, string), Lazy<byte[]>> s_spirv = new();

    /// <summary>Reflects the stages, compiling only code not seen before. Compile errors throw.</summary>
    public static ShaderPassReflection Compile(IReadOnlyList<(ShaderStage Stage, string Code)> stages)
    {
        if (s_spirv.Count > MaximumCachedStages) s_spirv.Clear();
        var compiled = ImmutableArray.CreateBuilder<(ShaderStage, byte[])>(stages.Count);
        foreach ((ShaderStage stage, string code) in stages)
        {
            if (string.IsNullOrWhiteSpace(code))
                throw new InvalidOperationException(NodeGraphStrings.ShaderError_EmptyCode);
            Lazy<byte[]> entry = s_spirv.GetOrAdd((stage, code),
                static key => new(() => ShaderAuthoringInterface.CompileSpirv(new ShaderSource(key.Item2), key.Item1)));
            try
            {
                compiled.Add((stage, entry.Value));
            }
            catch
            {
                // The cache keeps only code that compiled; a compiler that could not start may recover.
                s_spirv.TryRemove((stage, code), out _);
                throw;
            }
        }

        return Merge(compiled.MoveToImmutable(), default);
    }

    /// <summary>The same stages read under the values that can size their blocks.</summary>
    public ShaderPassReflection Specialize(ImmutableArray<SpecializationConstant> constants)
        => Merge(Stages, constants);

    private static ShaderPassReflection Merge(ImmutableArray<(ShaderStage Stage, byte[] Spirv)> stages,
        ImmutableArray<SpecializationConstant> constants)
    {
        var descriptors = new SortedDictionary<int, ShaderDescriptorLayout>();
        var pushConstants = new Dictionary<int, ShaderValueLayout>();
        var specializations = new SortedDictionary<uint, (ShaderSpecializationLayout Layout, ShaderStage Stages)>();
        string? pushConstantName = null;
        int pushConstantSize = 0;
        ImmutableArray<ShaderColorOutputLayout> colors = [];
        foreach ((ShaderStage stage, byte[] spirv) in stages)
        {
            ShaderAuthoringInterface shader = ShaderAuthoringInterface.Read(spirv, stage, constants);
            foreach (ShaderDescriptorLayout descriptor in shader.Descriptors)
            {
                // One stage may read what another stage writes, and the binding has to allow both.
                descriptors[descriptor.Binding] = descriptors.TryGetValue(descriptor.Binding, out ShaderDescriptorLayout? other)
                    ? other with
                    {
                        Access = other.Access | descriptor.Access,
                        ReadOnly = other.ReadOnly && descriptor.ReadOnly,
                        MinimumSize = Math.Max(other.MinimumSize, descriptor.MinimumSize)
                    }
                    : descriptor;
            }

            foreach (ShaderValueLayout member in shader.PushConstants)
                pushConstants.TryAdd(member.Offset, member);
            pushConstantName ??= shader.PushConstantName;
            pushConstantSize = Math.Max(pushConstantSize, shader.PushConstantSize);
            if (stage == ShaderStage.Fragment) colors = [.. shader.ColorOutputs.OrderBy(output => output.Location)];
            foreach (ShaderSpecializationLayout constant in shader.SpecializationConstants)
            {
                specializations[constant.ConstantId] = specializations.TryGetValue(constant.ConstantId, out var known)
                    ? (known.Layout, known.Stages | stage) : (constant, stage);
            }
        }

        return new([.. descriptors.Values], pushConstantName,
            [.. pushConstants.Values.OrderBy(member => member.Offset)], pushConstantSize, colors,
            [.. specializations.Values], stages);
    }
}

/// <summary>Finds what the renderer would refuse while a node can still say so.</summary>
/// <remarks>This reads the same optimized code the renderer compiles, without a device, so a mistake shows when it is
/// made rather than when a frame reaches it. What only a device can refuse arrives through
/// <see cref="ShaderGraphDescription.ExecutionObserver"/>.</remarks>
internal static class ShaderGraphPreflight
{
    private const int MaximumCachedStages = 256;

    private static readonly ConcurrentDictionary<(ShaderStage, string), Lazy<byte[]>> s_optimized = new();
    private static readonly ConditionalWeakTable<ShaderProgram, IReadOnlyDictionary<ShaderStage, ShaderInterface>> s_interfaces = new();

    public static void Validate(ShaderGraphDescription graph)
    {
        // Padding no rectangle can hold would be refused while the frame is being put together.
        graph.TransformBounds(new Rect(0, 0, 1, 1));
        foreach (ShaderPassDescription pass in graph.Passes)
        {
            if (pass.Program is { } program)
                ShaderInterface.ValidateProgram(s_interfaces.GetValue(program, ReadInterfaces), pass);
        }
    }

    private static IReadOnlyDictionary<ShaderStage, ShaderInterface> ReadInterfaces(ShaderProgram program)
    {
        if (s_optimized.Count > MaximumCachedStages) s_optimized.Clear();
        return program.Stages.ToDictionary(stage => stage.Key, stage =>
        {
            Lazy<byte[]> entry = s_optimized.GetOrAdd((stage.Key, stage.Value.Text), static key => new(() =>
            {
                using var compiler = new VulkanShaderCompiler();
                return compiler.CompileToSpirv(key.Item2, key.Item1);
            }));
            try
            {
                return new ShaderInterface(entry.Value, stage.Key, program.SpecializationConstants);
            }
            catch
            {
                s_optimized.TryRemove((stage.Key, stage.Value.Text), out _);
                throw;
            }
        });
    }
}
