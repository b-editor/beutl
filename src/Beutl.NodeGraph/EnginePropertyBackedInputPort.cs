using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
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

    protected void UnbindProperty()
    {
        if (_property != null)
        {
            _property.Edited -= OnTargetEdited;
            if (!Connection.IsNull && _property.Expression is NodePortExpression<T> && !HasOtherConnectedInput())
            {
                using var suppression = RecordingSuppression.Enter();
                _property.Expression = null;
            }
        }

        _property = null;
        Property = null;
    }

    private void OnTargetEdited(object? sender, EventArgs e) => RaiseEdited();

    private bool HasOtherConnectedInput()
    {
        GraphNode? node = this.FindHierarchicalParent<GraphNode>();
        IEnumerable<IInputPort>? inputs = node?.FindHierarchicalParent<GraphModel>() is { } graph
            ? graph.EnumerateConnectedInputs() : node?.GetConnectedInputs();
        // Rebinding can be in progress, so compare live targets rather than the topology cache.
        return inputs?.Any(input => input != this
            && ReferenceEquals(input.Property?.GetEngineProperty(), _property)) == true;
    }

    private void UpdateExpression()
    {
        if (_property == null || !_property.SupportsExpression) return;
        // Observers must update their previous-expression state, but this derived change must not
        // be recorded separately from the connection (or replayed on a replaced object).
        using var suppression = RecordingSuppression.Enter();
        if (!Connection.IsNull && _property.Expression is not NodePortExpression<T>)
            _property.Expression = new NodePortExpression<T>();
        else if (Connection.IsNull && _property.Expression is NodePortExpression<T> && !HasOtherConnectedInput())
            _property.Expression = null;
    }

    void IConnectionExpressionController.UpdateExpression(bool enabled)
    {
        if (enabled)
        {
            UpdateExpression();
        }
        else if (_property?.Expression is NodePortExpression<T>)
        {
            using var suppression = RecordingSuppression.Enter();
            _property.Expression = null;
        }
    }

    public void CopyFrom(IItemValue itemValue)
    {
        if (itemValue is not ItemValue<T> typed) return;
        if (!Connection.IsNull && _property?.Expression is NodePortExpression<T> exp)
        {
            exp.Value = typed.Value;
        }
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs args)
    {
        base.OnPropertyChanged(args);
        if (args is CorePropertyChangedEventArgs coreArgs &&
            coreArgs.Property.Id == ConnectionProperty.Id)
        {
            UpdateExpression();
        }
    }
}

/// <summary>Identifies an expression supplied by a node connection rather than a user formula.</summary>
public interface INodePortExpression : IExpression;

[JsonConverter(typeof(NodePortExpressionJsonConverter))]
public class NodePortExpression<T> : IExpression<T>, INodePortExpression
{
    public T? Value { get; set; }

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
}

// 空のオブジェクトを書き込み、空のExpressionを生成するだけのコンバーター
internal class NodePortExpressionJsonConverter : JsonConverter<IExpression>
{
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.GetGenericTypeDefinition() == typeof(NodePortExpression<>);
    }

    public override IExpression? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var node = JsonNode.Parse(ref reader);
        if (node is not JsonObject) throw new JsonException();

        // typeToConvertはNodePortExpression
        return Activator.CreateInstance(typeToConvert) as IExpression;
    }

    public override void Write(Utf8JsonWriter writer, IExpression value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteEndObject();
    }
}
