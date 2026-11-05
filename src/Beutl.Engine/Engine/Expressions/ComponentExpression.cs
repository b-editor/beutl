using System.Diagnostics.CodeAnalysis;
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

public sealed class ComponentExpression<TValue, TComponent> : IReplacingExpression<TComponent>, IComponentExpression
{
    private static readonly ComponentSet<TValue, TComponent> s_set = ComponentAnimations.GetComponentSet<TValue, TComponent>();

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
    public string ExpressionString => _expressionString ??= _source.WithMemberOfResult(Component);

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

    public TComponent Evaluate(ExpressionContext context)
    {
        if (ComponentIndex < 0)
            throw new ExpressionException($"{typeof(TValue).Name} has no component '{Component}'.");

        return s_set.Components[ComponentIndex].Get(_source.Evaluate(context));
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
}
