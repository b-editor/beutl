using System.Text.Json.Nodes;
using Beutl.Animation;
using Beutl.Engine.Expressions;
using Beutl.Serialization;
using Beutl.Validation;

namespace Beutl.Engine;

public class SimpleProperty<T>(T defaultValue, IValidator<T>? validator = null)
    : PropertyBase<T>(defaultValue, validator)
{
    public override bool IsAnimatable => false;

    /// <summary>
    /// Gets whether the property takes an expression. A property whose value has no meaning that varies
    /// with time, such as one that shapes a structure, sets this to <see langword="false"/>.
    /// </summary>
    public bool SupportsExpression { get; init; } = true;

    private protected override bool SupportsExpressionCore => SupportsExpression;

    public override IAnimation<T>? Animation
    {
        get => null;
        set
        {
            if (value != null)
            {
                throw new InvalidOperationException(
                    $"Property '{Name}' does not support animations. Use Property.CreateAnimatable<T>() to create animatable properties.");
            }
        }
    }

    public override IExpression<T>? Expression
    {
        get => base.Expression;
        set
        {
            if (value != null && !SupportsExpression)
            {
                throw new InvalidOperationException($"Property '{Name}' does not support expressions.");
            }

            base.Expression = value;
        }
    }

    public override string ToString() =>
        Expression is { } expression
            ? $"{Name}: {CurrentValue} (Default: {DefaultValue}, Simple, Expression: {expression.ExpressionString})"
            : $"{Name}: {CurrentValue} (Default: {DefaultValue}, Simple)";

    private protected override void ResolveDeserializedReference(ICoreSerializationContext context, T value)
    {
        if (value is IReference { IsNull: false } reference)
        {
            context.Resolve(reference.Id,
                resolved => CurrentValue = (T)reference.Resolved((CoreObject)resolved));
        }
    }

    public override void DeserializeExpression(JsonNode expressionNode)
    {
        if (!SupportsExpression) return;

        base.DeserializeExpression(expressionNode);
    }
}
