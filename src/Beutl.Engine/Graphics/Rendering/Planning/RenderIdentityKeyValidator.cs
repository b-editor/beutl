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
    ];

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
        if (retainsLifetimeOrCapability || mutablePayload || capturedDelegate)
        {
            throw new ArgumentException(IdentityRejection, parameterName);
        }
    }

    /// <summary>
    /// Validates a statically typed state against the identity-key rules, recursing through tuple elements.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The walk descends through <c>ValueTuple</c>/<c>Tuple</c> elements to any nesting depth, because a state
    /// carrying more than one value has to be a tuple. It stops at any other aggregate: a custom type's field
    /// set is not part of this API's vocabulary, so such a state is validated as one value and the
    /// lightweight-immutable-key rule governs what it holds. A sealed class holding a mutable field or a
    /// capturing delegate therefore still passes, and remains an enumerated identity channel in
    /// <c>docs/specs/004-gpu-pass-fusion/contracts/public-api.md</c>.
    /// </para>
    /// <para>
    /// Everything decidable from the closed type is decided once per closed type and costs nothing per call.
    /// An element whose declared type is <see cref="object"/>, an interface, an unsealed class, or a delegate
    /// cannot be decided that way — a delegate's capture is a property of the instance — so those are read
    /// through <see cref="ITuple"/>, which boxes each tuple level on the path to them. No state recorded by
    /// this repository declares such an element, so the recording path allocates exactly as it did before.
    /// </para>
    /// </remarks>
    public static void ThrowIfInvalidState<TState>(in TState state, string parameterName)
        where TState : notnull
    {
        if (StateShape<TState>.TypeRejection is { } reason)
            throw new ArgumentException(reason, parameterName);

        if (!typeof(TState).IsValueType)
            ThrowIfInvalid(state!, parameterName);

        StateElement[] undecided = StateShape<TState>.UndecidedElements;
        if (undecided.Length != 0)
            ThrowIfAnyUndecidedElementIsInvalid(state!, undecided, parameterName);
    }

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
        internal static readonly string? TypeRejection;
        internal static readonly StateElement[] UndecidedElements;

        static StateShape()
        {
            Type type = typeof(TState);
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
