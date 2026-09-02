using System.Runtime.CompilerServices;

namespace Beutl.Graphics.Rendering.Requests;

internal sealed class StructuralFragmentIdentity : IEquatable<StructuralFragmentIdentity>
{
    private readonly RenderFragmentKind _kind;
    private readonly RenderValueCardinality _cardinality;
    private readonly bool _contributesValuesToTarget;
    private readonly bool _canBeUsedAsValueInput;
    private readonly bool _hasTargetEffects;
    private readonly bool _potentiallyWritesTarget;
    private readonly bool _hasOpaqueExternalWork;
    private readonly int[] _inputs;
    private readonly Component[] _components;

    private StructuralFragmentIdentity(
        RenderFragmentReference reference,
        int[] inputs,
        Component[] components)
    {
        _kind = reference.Kind;
        _cardinality = reference.ValueCardinality;
        _contributesValuesToTarget = reference.ContributesValuesToTarget;
        _canBeUsedAsValueInput = reference.CanBeUsedAsValueInput;
        _hasTargetEffects = reference.HasTargetEffects;
        _potentiallyWritesTarget = reference.PotentiallyWritesTarget;
        _hasOpaqueExternalWork = reference.HasOpaqueExternalWork;
        _inputs = inputs;
        _components = components;
    }

    public static StructuralFragmentIdentity Create(
        RenderFragmentReference reference,
        IReadOnlyDictionary<RenderFragmentReference, int> indexes)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(indexes);
        int[] inputs = reference.Inputs.Length == 0 ? [] : new int[reference.Inputs.Length];
        for (int index = 0; index < reference.Inputs.Length; index++)
        {
            if (!indexes.TryGetValue(reference.Inputs[index], out inputs[index]))
            {
                throw new InvalidOperationException(
                    "A structural-plan input is not part of the recorded graph.");
            }
        }

        ComponentBuilder components = ComponentBuilder.Rent();
        if (reference.Kind is RenderFragmentKind.Shader or RenderFragmentKind.Opacity
            && reference.Inputs.Length == 1)
        {
            components.AddBoolean(ExecutionIslandPlanner.HasCompatibleMergeScale(
                reference.Inputs[0],
                reference));
            if (reference.Kind == RenderFragmentKind.Opacity)
            {
                components.AddBoolean(ExecutionIslandPlanner.HasCompatibleOpacityFusionMetadata(
                    reference.Inputs[0],
                    reference));
            }
        }
        AddPayloadComponents(reference, ref components);
        return new StructuralFragmentIdentity(reference, inputs, components.Build());
    }

    public bool Equals(StructuralFragmentIdentity? other)
        => other is not null
           && _kind == other._kind
           && _cardinality.Equals(other._cardinality)
           && _contributesValuesToTarget == other._contributesValuesToTarget
           && _canBeUsedAsValueInput == other._canBeUsedAsValueInput
           && _hasTargetEffects == other._hasTargetEffects
           && _potentiallyWritesTarget == other._potentiallyWritesTarget
           && _hasOpaqueExternalWork == other._hasOpaqueExternalWork
           && _inputs.AsSpan().SequenceEqual(other._inputs)
           && _components.AsSpan().SequenceEqual(other._components);

    public override bool Equals(object? obj)
        => obj is StructuralFragmentIdentity other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(_kind);
        hash.Add(_cardinality);
        hash.Add(_contributesValuesToTarget);
        hash.Add(_canBeUsedAsValueInput);
        hash.Add(_hasTargetEffects);
        hash.Add(_potentiallyWritesTarget);
        hash.Add(_hasOpaqueExternalWork);
        foreach (int input in _inputs)
            hash.Add(input);
        foreach (Component component in _components)
            hash.Add(component);
        return hash.ToHashCode();
    }

    private static void AddPayloadComponents(
        RenderFragmentReference reference,
        ref ComponentBuilder components)
    {
        switch (reference.Payload)
        {
            case null:
                return;
            case OpacityRenderFragmentPayload opacity:
                components.AddReference(opacity.FusionDescription.StructuralIdentity);
                components.AddBoolean(opacity.Opacity is >= 0 and <= 1);
                return;
            case BlendRenderFragmentPayload:
                return;
            case OpacityMaskRenderFragmentPayload mask:
                AddResourceType(mask.Mask, ref components);
                return;
            case ShaderRenderFragmentPayload shader:
                components.AddReference(shader.Description.StructuralIdentity);
                AddWorkingScalePolicy(shader.WorkingScalePolicy, ref components);
                return;
            case GeometryRenderFragmentPayload geometry:
                components.AddReference(geometry.Description.StructuralIdentity);
                AddWorkingScalePolicy(geometry.WorkingScalePolicy, ref components);
                return;
            case LayerRenderFragmentPayload layer:
                components.AddBoolean(layer.Domain.HasValue);
                components.AddBoolean(layer.DomainIsQueryFootprint);
                return;
            case TargetLayerScopeRenderFragmentPayload targetLayer:
                components.AddBoolean(targetLayer.Region.Kind != TargetRegionKind.Empty);
                return;
            case OpaqueRenderFragmentPayload opaque:
                AddOpaqueStructuralIdentity(opaque, ref components);
                components.AddReference(opaque.Description.Bounds.StructuralIdentity);
                components.AddReference(opaque.Description.HitTest.StructuralIdentity);
                components.AddReference(opaque.Description.Scale.StructuralIdentity);
                components.AddReference(opaque.Description.InputDemand.StructuralIdentity);
                AddInputReadbacks(opaque.InputReadbacks, ref components);
                AddResourceTypes(opaque.Description.Resources, ref components);
                return;
            case FilterEffectSegmentRenderFragmentPayload effectItem:
                AddWorkingScalePolicy(effectItem.WorkingScalePolicy, ref components);
                components.AddInt32(effectItem.StreamInputCount);
                // Whether the segment holds an imperative callback decides why its island ends, and the
                // boundary reason is part of the plan being cached. Two segments that agree on everything
                // else would otherwise share a plan whose classification contradicts one of their graphs.
                components.AddBoolean(effectItem.HasImperativeItem);
                return;
            case MaterializedInputRenderFragmentPayload input:
                components.AddReference(input.Description.HitTest.StructuralIdentity);
                return;
            case TargetCaptureRenderFragmentPayload capture:
                AddTargetCaptureComponents(capture.Description, ref components);
                return;
            case BuiltInBackdropCaptureRenderFragmentPayload capture:
                AddTargetCaptureComponents(capture.Description, ref components);
                return;
            case TargetScopeRenderFragmentPayload scope:
                AddTargetScopeComponents(scope.Description, ref components);
                return;
            case RawTargetScopeRenderFragmentPayload scope:
                components.AddReference(scope.Description.DefinitionFingerprint);
                components.AddReference(scope.Description.Bounds.StructuralIdentity);
                components.AddReference(scope.Description.HitTest.StructuralIdentity);
                components.AddReference(scope.Description.Scale.StructuralIdentity);
                AddResourceTypes(scope.Description.Resources, ref components);
                return;
            case RawTargetCommandRenderFragmentPayload command:
                components.AddReference(command.Description.DefinitionFingerprint);
                components.AddReference(command.Description.HitTest.StructuralIdentity);
                AddResourceTypes(command.Description.Resources, ref components);
                return;
            case TargetCommandRenderFragmentPayload command:
                components.AddReference(command.Description.DefinitionFingerprint);
                components.AddEnum(command.Description.Access);
                AddInputReadbacks(command.InputReadbacks, ref components);
                components.AddReference(command.Description.HitTest.StructuralIdentity);
                AddResourceTypes(command.Description.Resources, ref components);
                return;
            default:
                throw new InvalidOperationException(
                    $"Render fragment kind '{reference.Kind}' has an unrecognized structural payload.");
        }
    }

    // Mirrors the members OpaqueRenderDescription.GetStructuralIdentity composes, one component each, so that
    // the identity carries them without the record struct that call would allocate on every frame.
    private static void AddOpaqueStructuralIdentity(
        OpaqueRenderFragmentPayload opaque,
        ref ComponentBuilder components)
    {
        OpaqueRenderDescription description = opaque.Description;
        components.AddEnum(opaque.Topology);
        components.AddReference(description.DefinitionFingerprint);
        components.AddEnum(description.DeviceGridSensitivity);
        components.AddEnum(description.BackendBoundary);
        components.AddBoolean(description.HasDirectReplayMaterializationContract);
        components.AddBoolean(description.DirectReplayAtExactIntegerReduction);
        components.AddBoolean(description.SupportsDirectDstOut);
    }

    private static void AddInputReadbacks(
        IReadOnlyList<RenderInputReadback> readbacks,
        ref ComponentBuilder components)
    {
        components.AddInt32(readbacks.Count);
        for (int index = 0; index < readbacks.Count; index++)
        {
            RenderInputReadback readback = readbacks[index];
            components.AddInt32(readback.StructuralKind);
            IReadOnlyList<int> valueIndices = readback.ValueIndices;
            components.AddInt32(valueIndices.Count);
            for (int valueIndex = 0; valueIndex < valueIndices.Count; valueIndex++)
                components.AddInt32(valueIndices[valueIndex]);
        }
    }

    private static void AddTargetCaptureComponents(
        TargetCaptureDescription description,
        ref ComponentBuilder components)
    {
        components.AddReference(description.HitTest.StructuralIdentity);
        components.AddReference(description.Scale.StructuralIdentity);
    }

    private static void AddWorkingScalePolicy(
        FilterEffectWorkingScalePolicy? policy,
        ref ComponentBuilder components)
    {
        components.AddBoolean(policy.HasValue);
        if (policy is { } value)
            components.AddReference(value.StructuralIdentity);
    }

    private static void AddTargetScopeComponents(
        TargetScopeDescription description,
        ref ComponentBuilder components)
    {
        components.AddReference(description.DefinitionFingerprint);
        components.AddReference(description.Bounds.StructuralIdentity);
        components.AddReference(description.HitTest.StructuralIdentity);
        components.AddReference(description.Scale.StructuralIdentity);
        components.AddBoolean(description.IsValueReplayMap);
        components.AddEnum(description.TransformSpace);
        components.AddBoolean(description.BuiltInBackdropCapturesBackingTarget);
        AddResourceTypes(description.Resources, ref components);
    }

    private static void AddResourceType(RenderResource resource, ref ComponentBuilder components)
    {
        components.AddInt32(1);
        components.AddReference(resource.GetType());
    }

    private static void AddResourceTypes(
        IReadOnlyList<RenderResource> resources,
        ref ComponentBuilder components)
    {
        components.AddInt32(resources.Count);
        for (int index = 0; index < resources.Count; index++)
            components.AddReference(resources[index].GetType());
    }

    private static void AddResourceTypes(
        IReadOnlyList<RenderResourceBinding> resources,
        ref ComponentBuilder components)
    {
        components.AddInt32(resources.Count);
        for (int index = 0; index < resources.Count; index++)
            components.AddReference(resources[index].Slot.ValueType);
    }

    /// <summary>
    /// One component of a fragment identity. A value component keeps its payload off the heap and carries the
    /// type it came from, so <see langword="false"/>, <c>0</c> and a zero-valued enum stay as distinct from
    /// one another as separate boxes of them are.
    /// </summary>
    private readonly struct Component : IEquatable<Component>
    {
        private readonly object? _reference;
        private readonly long _value;
        private readonly bool _isValue;

        private Component(object? reference, long value, bool isValue)
        {
            _reference = reference;
            _value = value;
            _isValue = isValue;
        }

        public static Component FromReference(object? value) => new(value, 0L, isValue: false);

        public static Component FromBoolean(bool value) => new(typeof(bool), value ? 1L : 0L, isValue: true);

        public static Component FromInt32(int value) => new(typeof(int), value, isValue: true);

        public static Component FromEnum<TEnum>(TEnum value)
            where TEnum : unmanaged, Enum
            => new(typeof(TEnum), ToInt64(value), isValue: true);

        public bool Equals(Component other)
            => _isValue == other._isValue
               && _value == other._value
               && Equals(_reference, other._reference);

        public override bool Equals(object? obj) => obj is Component other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(_isValue, _reference, _value);

        // The read must match the enum's storage width: a wider one would take in bytes that are not part
        // of the value. Reading each width unsigned still maps distinct members of a type to distinct longs.
        private static long ToInt64<TEnum>(TEnum value)
            where TEnum : unmanaged, Enum
            => Unsafe.SizeOf<TEnum>() switch
            {
                1 => Unsafe.As<TEnum, byte>(ref value),
                2 => Unsafe.As<TEnum, ushort>(ref value),
                4 => Unsafe.As<TEnum, uint>(ref value),
                _ => Unsafe.As<TEnum, long>(ref value),
            };
    }

    /// <summary>
    /// Collects one fragment's components into a scratch buffer reused across fragments, then hands back a
    /// right-sized copy. The buffer is detached for the duration of a build, so a nested build takes its own.
    /// </summary>
    private struct ComponentBuilder
    {
        private const int InitialCapacity = 16;

        [ThreadStatic]
        private static Component[]? s_scratch;

        private Component[] _buffer;
        private int _count;

        private ComponentBuilder(Component[] buffer)
        {
            _buffer = buffer;
            _count = 0;
        }

        public static ComponentBuilder Rent()
        {
            Component[]? scratch = s_scratch;
            s_scratch = null;
            return new ComponentBuilder(scratch ?? new Component[InitialCapacity]);
        }

        public void AddReference(object? value) => Add(Component.FromReference(value));

        public void AddBoolean(bool value) => Add(Component.FromBoolean(value));

        public void AddInt32(int value) => Add(Component.FromInt32(value));

        public void AddEnum<TEnum>(TEnum value)
            where TEnum : unmanaged, Enum
            => Add(Component.FromEnum(value));

        public Component[] Build()
        {
            Component[] components = _count == 0 ? [] : _buffer.AsSpan(0, _count).ToArray();
            s_scratch = _buffer;
            return components;
        }

        private void Add(Component component)
        {
            if (_count == _buffer.Length)
                Array.Resize(ref _buffer, _buffer.Length * 2);
            _buffer[_count++] = component;
        }
    }
}
