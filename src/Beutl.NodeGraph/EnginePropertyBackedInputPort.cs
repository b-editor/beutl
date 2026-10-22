using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Beutl.Animation;
using Beutl.Editor;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.Extensibility;
using Beutl.NodeGraph.Composition;
using Beutl.PropertyAdapters;

namespace Beutl.NodeGraph;

public interface IEnginePropertyBackedInputPort
{
    void CopyFrom(IItemValue itemValue);
}

internal interface IConnectionExpressionController
{
    void UpdateExpression(bool enabled);
}

public class EnginePropertyBackedInputPort<T> : InputPort<T>, IEnginePropertyBackedInputPort, IConnectionExpressionController
{
    private IProperty<T>? _property;
    // Until #2885 step 4, a TimeSpan port drives a MediaTime property, as files name its port TimeSpan.
    private IExpressionProperty<MediaTime>? _timeProperty;

    protected EnginePropertyBackedInputPort()
    {
    }

    public EnginePropertyBackedInputPort(EngineObject obj, IProperty<T> property)
    {
        BindProperty(obj, property);
    }

    protected void BindProperty(EngineObject obj, IProperty<T> property)
    {
        if (ReferenceEquals(_property, property)) return;
        UnbindProperty();
        Name = property.Name;
        Display = property.GetAttributes()?.OfType<DisplayAttribute>().FirstOrDefault();
        _property = property;
        property.Edited += OnTargetEdited;
        IPropertyAdapter<T> adapter;
        if (property is AnimatableProperty<T> animatableProperty)
        {
            adapter = new AnimatablePropertyAdapter<T>(animatableProperty, obj);
        }
        else if (property is SimpleProperty<T> simpleProperty)
        {
            adapter = new SimplePropertyAdapter<T>(simpleProperty, obj);
        }
        else
        {
            adapter = new EnginePropertyAdapter<T>(property, obj);
        }

        Property = adapter;
        if (!Connection.IsNull) UpdateExpression();
    }

    private protected void BindTimeProperty(EngineObject obj, IExpressionProperty<MediaTime> property)
    {
        if (ReferenceEquals(_timeProperty, property)) return;
        UnbindProperty();
        Name = property.Name;
        Display = property.GetAttributes()?.OfType<DisplayAttribute>().FirstOrDefault();
        _timeProperty = property;
        property.Edited += OnTargetEdited;
        Property = (IPropertyAdapter<T>)(object)new TimeSpanPropertyAdapter(property, obj);
        if (!Connection.IsNull) UpdateExpression();
    }

    protected void UnbindProperty()
    {
        if (_property != null)
        {
            _property.Edited -= OnTargetEdited;
            if (!Connection.IsNull && _property is IExpressionProperty<T> property)
                RestoreReplaced(property);
        }
        else if (_timeProperty != null)
        {
            _timeProperty.Edited -= OnTargetEdited;
            if (!Connection.IsNull)
                RestoreReplaced(_timeProperty);
        }

        _property = null;
        _timeProperty = null;
        Property = null;
    }

    private void OnTargetEdited(object? sender, EventArgs e) => RaiseEdited();

    private bool HasOtherConnectedInput(bool acceptedOnly = false)
    {
        GraphNode? node = this.FindHierarchicalParent<GraphNode>();
        IEnumerable<IInputPort>? inputs = node?.FindHierarchicalParent<GraphModel>() is { } graph
            ? graph.EnumerateConnectedInputs() : node?.GetConnectedInputs();
        IProperty? target = (IProperty?)_property ?? _timeProperty;
        // Rebinding can be in progress, so compare live targets rather than the topology cache.
        return inputs?.Any(input => input != this
            && ReferenceEquals(input.Property?.GetEngineProperty(), target)
            && (!acceptedOnly || input.FindHierarchicalParent<GraphNode>()?.CanConnectInput(input) == true)) == true;
    }

    private void UpdateExpression()
    {
        if (_timeProperty != null)
            UpdateExpression(_timeProperty);
        else if (_property is IExpressionProperty<T> property)
            UpdateExpression(property);
    }

    private void UpdateExpression<TValue>(IExpressionProperty<TValue> property)
    {
        // Observers must update their previous-expression state, but this derived change must not
        // be recorded separately from the connection (or replayed on a replaced object). The user's
        // expression rides along in the port expression, so undoing the connection gives it back.
        using var suppression = HistoryReplay.Enter();
        if (!Connection.IsNull && property.Expression is not NodePortExpression<TValue>)
            property.Expression = new NodePortExpression<TValue> { Replaced = property.Expression };
        else if (Connection.IsNull && property.Expression is NodePortExpression<TValue> expression && !HasOtherConnectedInput())
            property.Expression = expression.Replaced;
    }

    // Gives the property back the expression the connection replaced, unless another input still drives it.
    private void RestoreReplaced<TValue>(IExpressionProperty<TValue> property, bool acceptedOnly = false)
    {
        if (property.Expression is NodePortExpression<TValue> expression && !HasOtherConnectedInput(acceptedOnly))
        {
            using var suppression = HistoryReplay.Enter();
            property.Expression = expression.Replaced;
        }
    }

    void IConnectionExpressionController.UpdateExpression(bool enabled)
    {
        if (enabled)
            UpdateExpression();
        else if (_timeProperty != null)
            RestoreReplaced(_timeProperty, acceptedOnly: true);
        else if (_property is IExpressionProperty<T> property)
            RestoreReplaced(property, acceptedOnly: true);
    }

    public void CopyFrom(IItemValue itemValue)
    {
        if (itemValue is not ItemValue<T> typed || Connection.IsNull) return;
        if (_timeProperty is { Expression: NodePortExpression<MediaTime> time } && typed.Value is TimeSpan span)
        {
            // A time past the range of MediaTime clamps.
            time.Value = MediaTime.FromTicks(span.Ticks, MidpointRounding.AwayFromZero);
        }
        else if (_property is IExpressionProperty<T> { Expression: NodePortExpression<T> exp })
        {
            exp.Value = typed.Value;
        }
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs args)
    {
        base.OnPropertyChanged(args);
        if (args.PropertyName == nameof(Connection))
        {
            UpdateExpression();
        }
    }
}

/// <summary>Identifies an expression supplied by a node connection rather than a user formula.</summary>
public interface INodePortExpression : IExpression;

// Lets the non-generic converter read and write the replaced expression.
internal interface IReplacedExpressionHolder
{
    JsonNode? ReplacedToNode();

    void ReplacedFromNode(JsonNode node);
}

[JsonConverter(typeof(NodePortExpressionJsonConverter))]
public class NodePortExpression<T> : IReplacingExpression<T>, INodePortExpression, IReplacedExpressionHolder
{
    public T? Value { get; set; }

    /// <summary>
    /// Gets the expression the connection replaced, which the property gets back once nothing is connected to it.
    /// </summary>
    public IExpression<T>? Replaced { get; internal set; }

    IExpression<T> IReplacingExpression<T>.WithReplaced(IExpression<T>? replaced)
        => new NodePortExpression<T> { Value = Value, Replaced = replaced };

    public string ExpressionString => "[NodePort Connected]";

    public bool Validate(out string? error)
    {
        error = null;
        return true;
    }

    public T Evaluate(ExpressionContext context)
    {
        return Value!;
    }

    // Keyframes below the connection are written under "Animations".
    JsonNode? IReplacedExpressionHolder.ReplacedToNode()
    {
        return Replaced is null or KeyFrameAnimation<T> ? null : Expression.ToNode(Replaced);
    }

    void IReplacedExpressionHolder.ReplacedFromNode(JsonNode node)
    {
        Replaced = Expression.CreateFromNode<T>(node);
    }
}

// The connection supplies the value, so only the expression it replaced is written.
internal class NodePortExpressionJsonConverter : JsonConverter<IExpression>
{
    private const string ReplacedKey = "Replaced";

    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.GetGenericTypeDefinition() == typeof(NodePortExpression<>);
    }

    public override IExpression? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var node = JsonNode.Parse(ref reader);
        if (node is not JsonObject obj) throw new JsonException();

        // typeToConvertはNodePortExpression
        var expression = Activator.CreateInstance(typeToConvert) as IExpression;
        if (expression is IReplacedExpressionHolder holder && obj[ReplacedKey] is { } replaced)
        {
            holder.ReplacedFromNode(replaced);
        }

        return expression;
    }

    public override void Write(Utf8JsonWriter writer, IExpression value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        if (value is IReplacedExpressionHolder holder && holder.ReplacedToNode() is { } replaced)
        {
            writer.WritePropertyName(ReplacedKey);
            replaced.WriteTo(writer, options);
        }

        writer.WriteEndObject();
    }
}
