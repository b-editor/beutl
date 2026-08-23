using System.Collections;
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
        Validate(captured, parameterName, new HashSet<object>(ReferenceEqualityComparer.Instance), depth: 0);
    }

    private const int MaxCaptureDepth = 8;

    private static void Validate(object value, string parameterName, HashSet<object> visited, int depth)
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

        if (!type.IsValueType && !visited.Add(value))
            return;

        foreach (FieldInfo field in type.GetFields(
                     BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (!type.IsValueType && !field.IsInitOnly)
            {
                throw new ArgumentException(
                    $"'{type.Name}.{field.Name}' can be assigned after this callback is recorded, so the "
                    + "callback's result can change while its structural identity does not. "
                    + IdentityRejection,
                    parameterName);
            }

            if (field.GetValue(value) is { } nested)
                Validate(nested, parameterName, visited, depth + 1);
        }
    }

    // Types whose contents cannot change, so following their fields would only reach private
    // implementation detail - an ImmutableArray's backing array reads as a mutable array from the outside.
    private static bool IsFixedLeaf(Type type)
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
                || definition == typeof(System.Collections.Immutable.ImmutableStack<>)
                || definition == typeof(ReadOnlyMemory<>)
                || definition == typeof(Nullable<>))
            {
                return true;
            }
        }

        return typeof(Type).IsAssignableFrom(type) || typeof(MemberInfo).IsAssignableFrom(type);
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
