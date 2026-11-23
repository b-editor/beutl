using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Beutl.Animation;

namespace Beutl.Engine.Expressions;

// One component of what another expression returns, which lets two properties share one expression.
public interface IComponentExpression : IExpression
{
    // The text of the shared expression; ExpressionString adds the component to it.
    string Source { get; }

    string Component { get; }

    // The component's place in its value, or -1 when the value has no such component.
    int ComponentIndex { get; }
}

// An expression whose value and keyframe globals come from a combined value (such as Point for X/Y).
internal interface ICombinedValueExpression<TComponent> : IExpression<TComponent>
{
    bool TryParseSameContext(
        string expressionString,
        [NotNullWhen(true)] out IExpression<TComponent>? expression,
        [NotNullWhen(false)] out string? error);
}

internal interface ICombinedValueSource
{
    IReadOnlyList<IProperty> Components { get; }
}

public sealed class ComponentExpression<TValue, TComponent>
    : IReplacingExpression<TComponent>, IComponentExpression, ICombinedValueExpression<TComponent>
{
    private static readonly ComponentSet<TValue, TComponent> s_set = ComponentAnimations.GetComponentSet<TValue, TComponent>();
    private static readonly ConditionalWeakTable<IProperty, CachedBelow> s_belowCache = [];

    private readonly StringExpression<TValue> _source;
    private string? _expressionString;

    [JsonConstructor]
    public ComponentExpression(string source, string component)
        : this(new StringExpression<TValue>(source), component, null)
    {
    }

    private ComponentExpression(StringExpression<TValue> source, string component, IExpression<TComponent>? replaced)
    {
        _source = source;
        Component = component;
        Replaced = replaced;
        ComponentIndex = Array.FindIndex(s_set.Components, definition => definition.Name == component);
    }

    public string Source => _source.ExpressionString;

    public string Component { get; }

    [JsonIgnore]
    public int ComponentIndex { get; }

    [JsonIgnore]
    public IExpression<TComponent>? Replaced { get; }

    // An expression of the component's own type, which the property's own editor can show and edit.
    [JsonIgnore]
    public string ExpressionString => _expressionString ??= StringExpressionParser.WithMemberOfResult(Source, Component);

    // One expression for each component of the value, which share the source and so compile it once.
    public static bool TryParse(string source, [NotNullWhen(true)] out IExpression<TComponent>[]? expressions,
        [NotNullWhen(false)] out string? error)
    {
        var shared = new StringExpression<TValue>(source);
        if (!shared.Validate(out error))
        {
            expressions = null;
            return false;
        }

        expressions = [.. s_set.Components.Select(definition =>
            new ComponentExpression<TValue, TComponent>(shared, definition.Name, null))];
        return true;
    }

    IExpression<TComponent> IReplacingExpression<TComponent>.WithReplaced(IExpression<TComponent>? replaced)
        => new ComponentExpression<TValue, TComponent>(_source, Component, replaced);

    bool ICombinedValueExpression<TComponent>.TryParseSameContext(
        string expressionString,
        [NotNullWhen(true)] out IExpression<TComponent>? expression,
        [NotNullWhen(false)] out string? error)
        => CombinedValueExpression<TValue, TComponent>.TryParseInCombinedContext(expressionString, out expression, out error);

    public TComponent Evaluate(ExpressionContext context)
    {
        if (ComponentIndex < 0)
            throw new ExpressionException($"{typeof(TValue).Name} has no component '{Component}'.");

        IExpression<TValue>? below = GetBelowExpression(context.CurrentProperty);
        return s_set.Components[ComponentIndex].Get(_source.Evaluate(context, below));
    }

    public bool Validate([NotNullWhen(false)] out string? error)
    {
        if (ComponentIndex < 0)
        {
            error = $"{typeof(TValue).Name} has no component '{Component}'.";
            return false;
        }

        return _source.Validate(out error);
    }

    public override string ToString() => ExpressionString;

    internal static IExpression<TValue>? GetBelowExpression(IProperty currentProperty)
    {
        if (currentProperty.GetOwnerObject() is not { } owner)
            return null;

        if (s_belowCache.TryGetValue(currentProperty, out CachedBelow? cached) && ReferenceEquals(cached.Owner, owner))
            return cached.Below;

        IProperty<TComponent>[]? siblings = FindSiblingProperties(currentProperty, owner);
        CombinedBelowExpression? below = siblings != null ? new CombinedBelowExpression(siblings) : null;
        s_belowCache.AddOrUpdate(currentProperty, new CachedBelow(owner, below));
        return below;
    }

    private static IProperty<TComponent>[]? FindSiblingProperties(IProperty currentProperty, CoreObject owner)
    {
        IReadOnlyList<IProperty> properties = owner.Properties;
        for (int i = 0; i < properties.Count; i++)
        {
            IProperty first = properties[i];
            if (first.GetAttributes()?.OfType<CombinedEditorAttribute>().FirstOrDefault() is not { } attribute)
                continue;

            string[] others = attribute.OtherNames;
            if (others.Length + 1 != s_set.Components.Length)
                continue;

            bool matches = ReferenceEquals(first, currentProperty);
            if (!matches)
            {
                for (int j = 0; j < others.Length; j++)
                {
                    if (others[j] == currentProperty.Name)
                    {
                        matches = true;
                        break;
                    }
                }
            }

            if (!matches || first is not IProperty<TComponent> typedFirst)
                continue;

            var siblings = new IProperty<TComponent>[s_set.Components.Length];
            siblings[0] = typedFirst;
            for (int j = 0; j < others.Length; j++)
            {
                IProperty<TComponent>? found = null;
                for (int k = 0; k < properties.Count; k++)
                {
                    if (properties[k].Name == others[j] && properties[k] is IProperty<TComponent> typed)
                    {
                        found = typed;
                        break;
                    }
                }

                if (found == null)
                    return null;

                siblings[j + 1] = found;
            }

            return siblings;
        }

        return null;
    }

    private sealed class CachedBelow(CoreObject owner, CombinedBelowExpression? below)
    {
        public CoreObject Owner { get; } = owner;
        public CombinedBelowExpression? Below { get; } = below;
    }

    // Composes the underlying values and exposes their keyframes to shared expressions on combined rows.
    private sealed class CombinedBelowExpression(IProperty<TComponent>[] siblings)
        : IExpression<TValue>, ICombinedValueSource, ILoopContinuation<TValue>
    {
        public IReadOnlyList<IProperty> Components => siblings;

        public string ExpressionString => string.Empty;

        public bool Validate(out string? error)
        {
            error = null;
            return true;
        }

        public TValue Evaluate(ExpressionContext context)
        {
            TValue result = default!;
            for (int i = 0; i < siblings.Length; i++)
            {
                result = s_set.Components[i].Set(result, EvaluateBelow(siblings[i], context));
            }

            return result;
        }

        public TValue Continue(ExpressionContext boundary, MediaTime time, bool loopOut)
        {
            TValue result = default!;
            for (int i = 0; i < siblings.Length; i++)
            {
                TComponent value = EvaluateBelow(siblings[i], boundary);
                if (FindKeyFrames(siblings[i]) is { KeyFrames.Count: > 1 } track)
                    value = KeyFrameContinuation.Continue(track, value, time, boundary.Time, loopOut);
                result = s_set.Components[i].Set(result, value);
            }

            return result;
        }

        private static KeyFrameAnimation<TComponent>? FindKeyFrames(IProperty<TComponent> property)
            => property is IExpressionProperty<TComponent> expressionProperty
                ? ExpressionChain.FindKeyFrames(expressionProperty.Expression) as KeyFrameAnimation<TComponent>
                : null;

        private static TComponent EvaluateBelow(IProperty<TComponent> property, ExpressionContext context)
        {
            if (property is IExpressionProperty<TComponent> expressionProperty
                && ExpressionChain.FindKeyFrames(expressionProperty.Expression) is IExpression<TComponent> keyFrames
                && keyFrames is not IKeyFrameAnimation { KeyFrames.Count: 0 })
            {
                TComponent value = keyFrames.Evaluate(context);
                if (value is not null)
                    return value;
            }

            return property.Value;
        }
    }
}

// A single-component expression whose value and keyframe globals come from the combined value (such as Point for
// Translate X/Y), preserving the aggregate evaluation context when a separated component expression is edited.
public sealed class CombinedValueExpression<TValue, TComponent>
    : IReplacingExpression<TComponent>, ICombinedValueExpression<TComponent>
{
    private readonly CompiledStringExpression<TComponent, TValue> _script;

    [JsonConstructor]
    public CombinedValueExpression(string expressionString)
        : this(new CompiledStringExpression<TComponent, TValue>(expressionString), null)
    {
    }

    private CombinedValueExpression(CompiledStringExpression<TComponent, TValue> script, IExpression<TComponent>? replaced)
    {
        _script = script;
        Replaced = replaced;
    }

    public string ExpressionString => _script.ExpressionString;

    [JsonIgnore]
    public IExpression<TComponent>? Replaced { get; }

    public static bool TryParse(
        string expressionString,
        [NotNullWhen(true)] out CombinedValueExpression<TValue, TComponent>? expression,
        [NotNullWhen(false)] out string? error)
    {
        var candidate = new CombinedValueExpression<TValue, TComponent>(expressionString);
        if (candidate.Validate(out error))
        {
            expression = candidate;
            return true;
        }

        expression = null;
        return false;
    }

    IExpression<TComponent> IReplacingExpression<TComponent>.WithReplaced(IExpression<TComponent>? replaced)
        => new CombinedValueExpression<TValue, TComponent>(_script, replaced);

    bool ICombinedValueExpression<TComponent>.TryParseSameContext(
        string expressionString,
        [NotNullWhen(true)] out IExpression<TComponent>? expression,
        [NotNullWhen(false)] out string? error)
        => TryParseInCombinedContext(expressionString, out expression, out error);

    internal static bool TryParseInCombinedContext(
        string expressionString,
        [NotNullWhen(true)] out IExpression<TComponent>? expression,
        [NotNullWhen(false)] out string? error)
    {
        string? contextError = null;
        if (StringExpressionParser.ReferencesValue(expressionString)
            && TryParse(expressionString, out CombinedValueExpression<TValue, TComponent>? contextual, out contextError))
        {
            expression = contextual;
            error = null;
            return true;
        }

        if (Expression.TryParse<TComponent>(expressionString, out StringExpression<TComponent>? plain, out error))
        {
            expression = plain;
            return true;
        }

        expression = null;
        // A script that reads from the combined value reports why it failed in that context.
        error = contextError ?? error;
        return false;
    }

    public TComponent Evaluate(ExpressionContext context)
    {
        IExpression<TValue>? below = ComponentExpression<TValue, TComponent>.GetBelowExpression(context.CurrentProperty);
        return _script.Evaluate(context, below);
    }

    public bool Validate([NotNullWhen(false)] out string? error) => _script.Validate(out error);

    public override string ToString() => ExpressionString;
}
