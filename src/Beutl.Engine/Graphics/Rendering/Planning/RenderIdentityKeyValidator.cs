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
