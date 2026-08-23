using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Beutl.Graphics.Effects;

namespace Beutl.Graphics.Rendering;

internal static class RenderIdentityKeyValidator
{
    private const string IdentityRejection =
        "A value captured by a metadata callback must be a lightweight, immutable CPU value and cannot retain "
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
            or RenderResourceRegistration
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

    private static bool IsRuntimeType(Type type) => type.GetType() == s_runtimeType;

    /// <summary>
    /// Rejects a captured value the callback's author could still change after recording.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A metadata callback is evaluated repeatedly and its structural identity is only its
    /// <see cref="MethodInfo"/>, so a capture that can change between evaluations makes the same identity
    /// stand for different bounds, scales or hit tests. The named collection types are not the only way to
    /// hold one: an ordinary class with a settable field does it too, which is what this reaches.
    /// </para>
    /// <para>
    /// The test is structural rather than a list: a reference type passes only when every instance field is
    /// <see langword="readonly"/> and holds something that passes in turn, so a shell whose fields cannot be
    /// reassigned and whose contents are themselves fixed is accepted however unfamiliar it is. A struct is
    /// not asked to be readonly - the callback reads whatever the display class holds either way, which is
    /// no more exposure than a captured <see cref="int"/> already carries - but what it points at is still
    /// followed.
    /// </para>
    /// </remarks>
    public static void ThrowIfMutableCapture(object captured, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(captured, parameterName);
        CapturePath path = default;
        Validate(captured, parameterName, path, depth: 0);
    }

    private const int MaxCaptureDepth = 8;

    // The references on the way down to the value being read. This runs once per recorded callback per
    // frame, so the walk carries its own cycle guard on the stack rather than allocating a set per call.
    [InlineArray(MaxCaptureDepth)]
    private struct CapturePath
    {
        private object? _element;
    }

    private static void Validate(object value, string parameterName, Span<object?> path, int depth)
    {
        ThrowIfInvalid(value, parameterName);

        Type type = value.GetType();
        if (IsFixedLeaf(type))
            return;

        if (depth >= MaxCaptureDepth)
        {
            // Deeper than any lightweight identity value is, and following it further would cost more than
            // reading the capture does.
            throw new ArgumentException(IdentityRejection, parameterName);
        }

        if (!type.IsValueType)
        {
            // A reference already on this path is a cycle, and one left over from a sibling branch was
            // accepted there, so either way it has nothing left to say.
            for (int index = 0; index < depth; index++)
            {
                if (ReferenceEquals(path[index], value))
                    return;
            }

            path[depth] = value;
        }

        foreach (FieldInfo field in GetInstanceFields(type))
        {
            if (!type.IsValueType && !field.IsInitOnly)
            {
                throw new ArgumentException(
                    $"'{type.Name}.{field.Name}' can be assigned after this callback is recorded, so the "
                    + "callback's result can change while its structural identity does not. "
                    + IdentityRejection,
                    parameterName);
            }

            // A field whose declared type is fixed and has no subtype cannot hold anything that is not, so
            // reading it would only box a number this walk has already accepted by its type.
            if (IsSettledCaptureType(field.FieldType))
                continue;

            if (field.GetValue(value) is { } nested)
                Validate(nested, parameterName, path, depth + 1);
        }
    }

    /// <summary>
    /// Gets whether a field declared as <paramref name="type"/> is accepted by its declaration alone, so a
    /// capture walk need not read its value.
    /// </summary>
    /// <remarks>
    /// A value type is its own runtime type and a sealed one has no subtype, so when either is fixed nothing
    /// can be stored there that this walk would reject.
    /// </remarks>
    internal static bool IsSettledCaptureType(Type type)
        => (type.IsValueType || type.IsSealed) && IsFixedLeaf(type);

    private static readonly ConcurrentDictionary<Type, FieldInfo[]> s_instanceFields = new();

    internal static FieldInfo[] GetInstanceFields(Type type)
        => s_instanceFields.GetOrAdd(
            type,
            static key => key.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));

    private static readonly ConcurrentDictionary<Type, bool> s_fixedLeaf = new();

    // Types whose contents cannot change, so following their fields would only reach private
    // implementation detail - an ImmutableArray's backing array reads as a mutable array from the outside.
    // ReadOnlyMemory is deliberately absent: it is a read-only view, and the array it ordinarily wraps stays
    // in the author's hands, so its contents are followed like any other capture. Nullable is absent for the
    // opposite reason - the struct rule below already decides it from the type it wraps.
    private static bool IsFixedLeaf(Type type) => s_fixedLeaf.GetOrAdd(type, static key => ComputeIsFixedLeaf(key));

    private static bool ComputeIsFixedLeaf(Type type)
    {
        if (type.IsPrimitive || type.IsEnum || type.IsPointer)
            return true;
        if (type == typeof(string) || type == typeof(decimal) || type == typeof(DateTime)
            || type == typeof(DateTimeOffset) || type == typeof(TimeSpan) || type == typeof(Guid)
            || type == typeof(Uri) || type == typeof(Version) || type == typeof(DBNull))
        {
            return true;
        }

        if (type.IsGenericType)
        {
            Type definition = type.GetGenericTypeDefinition();
            if (definition == typeof(System.Collections.Immutable.ImmutableArray<>)
                || definition == typeof(System.Collections.Immutable.ImmutableList<>)
                || definition == typeof(System.Collections.Immutable.ImmutableHashSet<>)
                || definition == typeof(System.Collections.Immutable.ImmutableSortedSet<>)
                || definition == typeof(System.Collections.Immutable.ImmutableDictionary<,>)
                || definition == typeof(System.Collections.Immutable.ImmutableSortedDictionary<,>)
                || definition == typeof(System.Collections.Immutable.ImmutableQueue<>)
                || definition == typeof(System.Collections.Immutable.ImmutableStack<>))
            {
                // What cannot change is the collection, not what it holds. A boxes[0].Value the author can
                // still assign reads through an immutable array exactly as it reads through a field, so the
                // exemption only stands when the elements are settled by their own declaration too.
                return type.GetGenericArguments().All(IsSettledCaptureType);
            }
        }

        if (typeof(Type).IsAssignableFrom(type) || typeof(MemberInfo).IsAssignableFrom(type))
            return true;

        // A struct reachable only by copy is fixed when every field it carries is fixed in turn: nothing the
        // callback's author still holds can reach into it. C# forbids a struct that contains itself, so this
        // descent terminates, and it stops at the first reference type, which is never fixed by its
        // declaration alone.
        if (type.IsValueType && !typeof(IDisposable).IsAssignableFrom(type))
        {
            foreach (FieldInfo field in GetInstanceFields(type))
            {
                if (!IsFixedLeaf(field.FieldType))
                    return false;
            }

            return true;
        }

        return false;
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
