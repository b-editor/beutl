using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using Beutl.Graphics.Effects;

namespace Beutl.Graphics.Rendering;

internal static class RenderIdentityKeyValidator
{
    private const string IdentityRejection =
        "A persistent render identity key must be a lightweight, immutable CPU identity and cannot retain "
        + "a resource, context, request graph, mutable payload, or captured delegate.";

    private const string FacadeRejection =
        "A persistent identity or pure metadata callback cannot retain an execution session or facade.";

    private static readonly Type[] s_facadeShapes =
    [
        typeof(RenderExecutionInput),
        typeof(RenderCallbackCanvas),
        typeof(OpaqueRenderSession),
        typeof(OpaqueRenderOutput),
        typeof(PaintedRenderCanvas),
        typeof(PaintedRenderSession),
        typeof(GeometrySession),
        typeof(ShaderExecutionContext),
        typeof(ShaderUniformWriter),
        typeof(ShaderResourceWriter),
        typeof(TargetScopeSession),
        typeof(TargetCommandSession),
        typeof(RawTargetScopeSession),
        typeof(RawTargetCommandSession),
    ];

    private static readonly Type[] s_retainedShapes =
    [
        typeof(RenderResource),
        typeof(RenderNodeContext),
        typeof(RenderRequest),
        typeof(RenderRequestOptions),
        typeof(RecordedRenderGraph),
        typeof(RecordedRenderGraphBuilder),
        typeof(RenderResourceSlot),
        typeof(RenderFragmentHandle),
        typeof(LoweredBrush),
        typeof(LoweredPen),
    ];

    private static readonly Type s_runtimeType = typeof(Type).GetType();

    public static void ThrowIfInvalid(object key, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(key, parameterName);

        bool retainsLifetimeOrCapability = key is IDisposable
            or RenderResource
            or RenderNodeContext
            or RenderRequest
            or RenderRequestOptions
            or RecordedRenderGraph
            or RecordedRenderGraphBuilder
            or RenderResourceSlot
            or RenderFragmentHandle
            or LoweredBrush
            or LoweredPen
            or RenderExecutionInput
            or RenderCallbackCanvas
            or OpaqueRenderSession
            or OpaqueRenderOutput
            or PaintedRenderCanvas
            or PaintedRenderSession
            or GeometrySession
            or ShaderExecutionContext
            or ShaderUniformWriter
            or ShaderResourceWriter
            or TargetScopeSession
            or TargetCommandSession
            or RawTargetScopeSession
            or RawTargetCommandSession;
        bool mutablePayload = key is Array || IsKnownMutableCollection(key.GetType());
        bool capturedDelegate = key is Delegate callback && CapturesState(callback);
        bool customType = key is Type type && !IsRuntimeType(type);
        if (retainsLifetimeOrCapability || mutablePayload || capturedDelegate || customType)
        {
            throw new ArgumentException(IdentityRejection, parameterName);
        }
    }

    /// <summary>
    /// Validates a statically typed state against the identity-key rules, recursing through every field.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The closed state type must be deeply immutable. Value types and sealed reference types are accepted only
    /// when all of their instance fields recursively satisfy the same rule; arrays, mutable collections,
    /// delegates, disposable resources, renderer facades, and open reference shapes are rejected.
    /// </para>
    /// <para>
    /// Tuple elements that require an instance-level identity check are read through <see cref="ITuple"/> after
    /// the closed-type check. This preserves precise diagnostics for dynamically typed tuple slots without
    /// allowing them to bypass the immutable-state requirement.
    /// </para>
    /// </remarks>
    public static void ThrowIfInvalidState<TState>(in TState state, string parameterName)
        where TState : notnull
    {
        if (!StateShape<TState>.IsDeeplyImmutable)
        {
            throw new ArgumentException(
                "A state-passing callback requires a copied, deeply immutable state value. "
                + "Use an immutable value/record snapshot, include an explicit version in that snapshot, "
                + "or use the request-local factory for mutable callback state.",
                parameterName);
        }

        if (StateShape<TState>.TypeRejection is { } reason)
            throw new ArgumentException(reason, parameterName);

        if (!typeof(TState).IsValueType)
            ThrowIfInvalid(state!, parameterName);

        StateElement[] undecided = StateShape<TState>.UndecidedElements;
        if (undecided.Length != 0)
            ThrowIfAnyUndecidedElementIsInvalid(state!, undecided, parameterName);

        IdentityShape identityShape = StateIdentityShape<TState>.Root;
        if (identityShape.RequiresTerminalValueValidation)
            identityShape.ThrowIfInvalidTerminalValues(state, parameterName);
    }

    private static bool IsDeeplyImmutableStateType(Type type, HashSet<Type> visiting)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type == typeof(IntPtr) || type == typeof(UIntPtr))
        {
            return false;
        }

        if (IsTerminalStateType(type))
        {
            return true;
        }

        if (type.IsArray
            || type.IsPointer
            || type.IsFunctionPointer
            || type.IsByRefLike
            || typeof(Delegate).IsAssignableFrom(type)
            || typeof(IDisposable).IsAssignableFrom(type)
            || IsKnownMutableCollection(type)
            || Array.Exists(s_facadeShapes, shape => shape.IsAssignableFrom(type))
            || Array.Exists(s_retainedShapes, shape => shape.IsAssignableFrom(type))
            || (!type.IsValueType && !type.IsSealed))
        {
            return false;
        }

        // A recursive type graph can produce a cyclic object graph. Reject it rather than making cache-key
        // comparison depend on reference identity or an author-provided cycle breaker.
        if (!visiting.Add(type))
            return false;

        try
        {
            foreach (FieldInfo field in GetInstanceFields(type))
            {
                if (!type.IsValueType && !field.IsInitOnly)
                    return false;
                if (!IsDeeplyImmutableStateType(field.FieldType, visiting))
                    return false;
            }

            return true;
        }
        finally
        {
            visiting.Remove(type);
        }
    }

    /// <summary>
    /// Compares validated state by its complete immutable field graph, without invoking author-provided
    /// equality members.
    /// </summary>
    public static bool StateEquals<TState>(in TState left, in TState right)
        where TState : notnull
        => StateIdentityShape<TState>.Root.AreEqual(left, right);

    /// <summary>
    /// Hashes validated state by its complete immutable field graph, without invoking author-provided hash
    /// members.
    /// </summary>
    public static int StateHashCode<TState>(in TState state)
        where TState : notnull
        => StateIdentityShape<TState>.Root.ComputeHashCode(state);

    private static FieldInfo[] GetInstanceFields(Type type)
    {
        var hierarchy = new Stack<Type>();
        for (Type? current = type;
             current is not null && current != typeof(object) && current != typeof(ValueType);
             current = current.BaseType)
        {
            hierarchy.Push(current);
        }

        var fields = new List<FieldInfo>();
        while (hierarchy.TryPop(out Type? current))
        {
            fields.AddRange(current.GetFields(
                BindingFlags.Instance
                | BindingFlags.Public
                | BindingFlags.NonPublic
                | BindingFlags.DeclaredOnly));
        }

        return [.. fields.OrderBy(static field => field.MetadataToken)];
    }

    private static class StateIdentityShape<TState>
        where TState : notnull
    {
        internal static readonly IdentityShape Root = IdentityShape.Create(typeof(TState), []);
    }

    private sealed class IdentityShape
    {
        private readonly TerminalIdentityKind _terminalKind;
        private readonly IdentityShape? _nullableValue;
        private readonly IdentityField[] _fields;

        private IdentityShape(
            TerminalIdentityKind terminalKind,
            IdentityShape? nullableValue,
            IdentityField[] fields)
        {
            _terminalKind = terminalKind;
            _nullableValue = nullableValue;
            _fields = fields;
            RequiresTerminalValueValidation = terminalKind == TerminalIdentityKind.Type
                                               || nullableValue?.RequiresTerminalValueValidation == true
                                               || fields.Any(static field =>
                                                   field.Shape.RequiresTerminalValueValidation);
        }

        public bool RequiresTerminalValueValidation { get; }

        public static IdentityShape Create(Type type, HashSet<Type> visiting)
        {
            Type? nullableType = Nullable.GetUnderlyingType(type);
            if (nullableType is not null)
            {
                return new IdentityShape(
                    TerminalIdentityKind.None,
                    nullableValue: Create(nullableType, visiting),
                    fields: []);
            }

            TerminalIdentityKind terminalKind = GetTerminalIdentityKind(type);
            if (terminalKind != TerminalIdentityKind.None)
                return new IdentityShape(terminalKind, nullableValue: null, fields: []);

            if (!visiting.Add(type))
            {
                throw new InvalidOperationException(
                    $"The validated render state type graph for '{type}' is recursive.");
            }

            try
            {
                IdentityField[] fields =
                [
                    .. GetInstanceFields(type).Select(field =>
                        new IdentityField(field, Create(field.FieldType, visiting))),
                ];
                return new IdentityShape(TerminalIdentityKind.None, nullableValue: null, fields);
            }
            finally
            {
                visiting.Remove(type);
            }
        }

        public bool AreEqual(object? left, object? right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left is null || right is null)
                return false;

            if (_nullableValue is not null)
            {
                // A non-null Nullable<T> boxes as T, so compare the boxed underlying values directly.
                return _nullableValue.AreEqual(left, right);
            }

            if (_terminalKind != TerminalIdentityKind.None)
                return TerminalEquals(_terminalKind, left, right);

            foreach (IdentityField field in _fields)
            {
                if (!field.Shape.AreEqual(field.Field.GetValue(left), field.Field.GetValue(right)))
                    return false;
            }

            return true;
        }

        public int ComputeHashCode(object? value)
        {
            if (value is null)
                return 0;

            if (_nullableValue is not null)
                return _nullableValue.ComputeHashCode(value);
            if (_terminalKind != TerminalIdentityKind.None)
                return TerminalHashCode(_terminalKind, value);

            int hash = 17;
            foreach (IdentityField field in _fields)
            {
                hash = unchecked((hash * 31) + field.Shape.ComputeHashCode(field.Field.GetValue(value)));
            }

            return hash;
        }

        public void ThrowIfInvalidTerminalValues(object? value, string parameterName)
        {
            if (value is null)
                return;

            if (_nullableValue is not null)
            {
                _nullableValue.ThrowIfInvalidTerminalValues(value, parameterName);
                return;
            }

            if (_terminalKind != TerminalIdentityKind.None)
            {
                if (_terminalKind == TerminalIdentityKind.Type && !IsRuntimeType((Type)value))
                {
                    throw new ArgumentException(
                        "A state-passing callback accepts Type values only when they are immutable runtime types.",
                        parameterName);
                }

                return;
            }

            foreach (IdentityField field in _fields)
            {
                field.Shape.ThrowIfInvalidTerminalValues(field.Field.GetValue(value), parameterName);
            }
        }
    }

    private readonly record struct IdentityField(FieldInfo Field, IdentityShape Shape);

    private enum TerminalIdentityKind : byte
    {
        None,
        Default,
        String,
        Single,
        Double,
        Decimal,
        DateTime,
        DateTimeOffset,
        Type,
    }

    private static bool IsTerminalStateType(Type type)
        => GetTerminalIdentityKind(type) != TerminalIdentityKind.None;

    private static TerminalIdentityKind GetTerminalIdentityKind(Type type)
    {
        if (type == typeof(float))
            return TerminalIdentityKind.Single;
        if (type == typeof(double))
            return TerminalIdentityKind.Double;
        if (type == typeof(decimal))
            return TerminalIdentityKind.Decimal;
        if (type == typeof(DateTime))
            return TerminalIdentityKind.DateTime;
        if (type == typeof(DateTimeOffset))
            return TerminalIdentityKind.DateTimeOffset;
        if (type == typeof(Type))
            return TerminalIdentityKind.Type;
        if (type == typeof(string))
            return TerminalIdentityKind.String;
        if (type.IsPrimitive
            || type.IsEnum
            || type == typeof(Guid)
            || type == typeof(TimeSpan))
        {
            return TerminalIdentityKind.Default;
        }

        return TerminalIdentityKind.None;
    }

    private static bool TerminalEquals(TerminalIdentityKind kind, object left, object right)
        => kind switch
        {
            TerminalIdentityKind.Single => BitConverter.SingleToInt32Bits((float)left)
                                           == BitConverter.SingleToInt32Bits((float)right),
            TerminalIdentityKind.Double => BitConverter.DoubleToInt64Bits((double)left)
                                           == BitConverter.DoubleToInt64Bits((double)right),
            TerminalIdentityKind.Decimal => DecimalBitsEqual((decimal)left, (decimal)right),
            TerminalIdentityKind.DateTime => ((DateTime)left).ToBinary() == ((DateTime)right).ToBinary(),
            TerminalIdentityKind.DateTimeOffset => ((DateTimeOffset)left).Ticks == ((DateTimeOffset)right).Ticks
                                                   && ((DateTimeOffset)left).Offset
                                                   == ((DateTimeOffset)right).Offset,
            TerminalIdentityKind.Type => ReferenceEquals(left, right),
            TerminalIdentityKind.String => string.Equals((string)left, (string)right, StringComparison.Ordinal),
            TerminalIdentityKind.Default => left.Equals(right),
            _ => throw new InvalidOperationException("A non-terminal state reached terminal equality."),
        };

    private static int TerminalHashCode(TerminalIdentityKind kind, object value)
        => kind switch
        {
            TerminalIdentityKind.Single => BitConverter.SingleToInt32Bits((float)value),
            TerminalIdentityKind.Double => BitConverter.DoubleToInt64Bits((double)value).GetHashCode(),
            TerminalIdentityKind.Decimal => DecimalBitsHashCode((decimal)value),
            TerminalIdentityKind.DateTime => ((DateTime)value).ToBinary().GetHashCode(),
            TerminalIdentityKind.DateTimeOffset => HashCode.Combine(
                ((DateTimeOffset)value).Ticks,
                ((DateTimeOffset)value).Offset.Ticks),
            TerminalIdentityKind.Type => RuntimeHelpers.GetHashCode(value),
            TerminalIdentityKind.String => StringComparer.Ordinal.GetHashCode((string)value),
            TerminalIdentityKind.Default => value.GetHashCode(),
            _ => throw new InvalidOperationException("A non-terminal state reached terminal hashing."),
        };

    private static bool DecimalBitsEqual(decimal left, decimal right)
    {
        Span<int> leftBits = stackalloc int[4];
        Span<int> rightBits = stackalloc int[4];
        decimal.GetBits(left, leftBits);
        decimal.GetBits(right, rightBits);
        return leftBits.SequenceEqual(rightBits);
    }

    private static int DecimalBitsHashCode(decimal value)
    {
        Span<int> bits = stackalloc int[4];
        decimal.GetBits(value, bits);
        int hash = 17;
        foreach (int bit in bits)
            hash = unchecked((hash * 31) + bit);
        return hash;
    }

    private static bool IsRuntimeType(Type type) => type.GetType() == s_runtimeType;

    private static void ThrowIfAnyUndecidedElementIsInvalid(
        object state,
        StateElement[] undecided,
        string parameterName)
    {
        foreach (StateElement element in undecided)
        {
            object? current = state;
            foreach (int index in element.Path)
            {
                if (current is not ITuple tuple || (uint)index >= (uint)tuple.Length)
                {
                    current = null;
                    break;
                }

                current = tuple[index];
            }

            if (current is null)
                continue;

            try
            {
                ThrowIfInvalid(current, parameterName);
            }
            catch (ArgumentException inner)
            {
                throw new ArgumentException(
                    $"{IdentityRejection} State element '{element.Display}' is one.",
                    parameterName,
                    inner);
            }
        }
    }

    public static bool CapturesState(Delegate callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return callback.GetInvocationList().Any(IsCapturedDelegate);
    }

    private static bool IsCapturedDelegate(Delegate callback)
    {
        if (callback.Target is null)
            return false;

        // Roslyn caches non-capturing lambdas on a sealed compiler-generated singleton and emits
        // an instance method for them. Accept only that stateless shape; display classes, derived
        // targets, and ordinary instance delegates remain rejected.
        Type targetType = callback.Target.GetType();
        return !targetType.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false)
               || !targetType.IsSealed
               || targetType.BaseType != typeof(object)
               || targetType.GetFields(
                   BindingFlags.Instance
                   | BindingFlags.Public
                   | BindingFlags.NonPublic
                   | BindingFlags.DeclaredOnly).Length != 0;
    }

    private readonly record struct StateElement(int[] Path, string Display);

    private enum StateElementVerdict
    {
        Decided,
        RequiresValue,
        RejectedIdentity,
        RejectedFacade,
    }

    private static class StateShape<TState>
    {
        internal static readonly bool IsDeeplyImmutable;
        internal static readonly string? TypeRejection;
        internal static readonly StateElement[] UndecidedElements;

        static StateShape()
        {
            Type type = typeof(TState);
            IsDeeplyImmutable = IsDeeplyImmutableStateType(type, []);
            if (type.IsValueType && !IsTupleType(type))
            {
                TypeRejection = DescribeTypeRejection(ClassifyElementType(type), $"The state type '{type}'");
                UndecidedElements = [];
                return;
            }

            var undecided = new List<StateElement>();
            string? rejection = null;
            WalkTupleElements(type, [], string.Empty, undecided, ref rejection);
            TypeRejection = rejection;
            UndecidedElements = rejection is null ? [.. undecided] : [];
        }
    }

    private static void WalkTupleElements(
        Type tupleType,
        int[] path,
        string display,
        List<StateElement> undecided,
        ref string? rejection)
    {
        if (!IsTupleType(tupleType))
            return;

        var elements = new List<(int[] Path, string Display, Type Type)>();
        int flatIndex = 0;
        CollectTupleElements(tupleType, path, display, ref flatIndex, elements);
        foreach ((int[] elementPath, string elementDisplay, Type elementType) in elements)
        {
            if (IsTupleType(elementType))
            {
                WalkTupleElements(elementType, elementPath, elementDisplay + ".", undecided, ref rejection);
                if (rejection is not null)
                    return;

                continue;
            }

            StateElementVerdict verdict = ClassifyElementType(elementType);
            if (verdict == StateElementVerdict.RequiresValue)
            {
                undecided.Add(new StateElement(elementPath, elementDisplay));
                continue;
            }

            if (DescribeTypeRejection(verdict, $"State element '{elementDisplay}' of type '{elementType}'")
                is { } elementRejection)
            {
                rejection = elementRejection;
                return;
            }
        }
    }

    /// <remarks>
    /// A <c>TRest</c> element does not become its own <see cref="ITuple"/> level: an eight-or-more element tuple
    /// reports its rest chain flattened into one index space, so the chain continues the current index.
    /// </remarks>
    private static void CollectTupleElements(
        Type tupleType,
        int[] path,
        string display,
        ref int flatIndex,
        List<(int[] Path, string Display, Type Type)> elements)
    {
        Type[] arguments = tupleType.GetGenericArguments();
        bool hasRest = arguments.Length == 8;
        int inlineCount = hasRest ? 7 : arguments.Length;
        for (int index = 0; index < inlineCount; index++)
        {
            int flat = flatIndex++;
            elements.Add(([.. path, flat], $"{display}Item{flat + 1}", arguments[index]));
        }

        if (hasRest && IsTupleType(arguments[7]))
            CollectTupleElements(arguments[7], path, display, ref flatIndex, elements);
    }

    private static string? DescribeTypeRejection(StateElementVerdict verdict, string subject)
        => verdict switch
        {
            StateElementVerdict.RejectedFacade => $"{FacadeRejection} {subject} is one.",
            StateElementVerdict.RejectedIdentity => $"{IdentityRejection} {subject} is one.",
            _ => null,
        };

    private static StateElementVerdict ClassifyElementType(Type type)
    {
        if (Array.Exists(s_facadeShapes, shape => shape.IsAssignableFrom(type)))
            return StateElementVerdict.RejectedFacade;

        if (typeof(IDisposable).IsAssignableFrom(type)
            || type.IsArray
            || IsKnownMutableCollection(type)
            || Array.Exists(s_retainedShapes, shape => shape.IsAssignableFrom(type)))
        {
            return StateElementVerdict.RejectedIdentity;
        }

        if (typeof(Delegate).IsAssignableFrom(type))
            return StateElementVerdict.RequiresValue;

        return type.IsValueType || type.IsSealed
            ? StateElementVerdict.Decided
            : StateElementVerdict.RequiresValue;
    }

    private static bool IsTupleType(Type type)
    {
        if (!type.IsGenericType)
            return false;

        Type definition = type.GetGenericTypeDefinition();
        return definition == typeof(ValueTuple<>)
               || definition == typeof(ValueTuple<,>)
               || definition == typeof(ValueTuple<,,>)
               || definition == typeof(ValueTuple<,,,>)
               || definition == typeof(ValueTuple<,,,,>)
               || definition == typeof(ValueTuple<,,,,,>)
               || definition == typeof(ValueTuple<,,,,,,>)
               || definition == typeof(ValueTuple<,,,,,,,>)
               || definition == typeof(Tuple<>)
               || definition == typeof(Tuple<,>)
               || definition == typeof(Tuple<,,>)
               || definition == typeof(Tuple<,,,>)
               || definition == typeof(Tuple<,,,,>)
               || definition == typeof(Tuple<,,,,,>)
               || definition == typeof(Tuple<,,,,,,>)
               || definition == typeof(Tuple<,,,,,,,>);
    }

    private static bool IsKnownMutableCollection(Type type)
    {
        for (Type? current = type; current is not null; current = current.BaseType)
        {
            if (current == typeof(ArrayList)
                || current == typeof(Hashtable)
                || current == typeof(System.Collections.Queue)
                || current == typeof(System.Collections.Stack))
            {
                return true;
            }

            if (!current.IsGenericType)
                continue;

            Type definition = current.GetGenericTypeDefinition();
            if (definition == typeof(List<>)
                || definition == typeof(Dictionary<,>)
                || definition == typeof(HashSet<>)
                || definition == typeof(SortedSet<>)
                || definition == typeof(Queue<>)
                || definition == typeof(Stack<>)
                || definition == typeof(LinkedList<>)
                || definition == typeof(SortedDictionary<,>)
                || definition == typeof(SortedList<,>)
                || definition == typeof(System.Collections.ObjectModel.Collection<>)
                || definition == typeof(System.Collections.ObjectModel.ObservableCollection<>)
                || definition == typeof(System.Collections.ObjectModel.ReadOnlyCollection<>)
                || definition == typeof(System.Collections.ObjectModel.ReadOnlyDictionary<,>)
                || definition == typeof(System.Collections.Concurrent.ConcurrentBag<>)
                || definition == typeof(System.Collections.Concurrent.ConcurrentQueue<>)
                || definition == typeof(System.Collections.Concurrent.ConcurrentStack<>)
                || definition == typeof(System.Collections.Concurrent.ConcurrentDictionary<,>))
            {
                return true;
            }
        }

        return false;
    }
}
