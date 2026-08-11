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
            or RenderExecutionInput
            or RenderCallbackCanvas
            or OpaqueRenderSession
            or OpaqueRenderOutput
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
