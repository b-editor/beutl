using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.Extensibility;
using Beutl.NodeTree.Rendering;
using Beutl.Operation;

namespace Beutl.NodeTree;

public interface IEnginePropertyBackedInputSocket
{
    void CopyFrom(IItemValue itemValue);
}

public class EnginePropertyBackedInputSocket<T> : InputSocket<T>, IEnginePropertyBackedInputSocket
{
    private readonly IProperty<T> _property;

    public EnginePropertyBackedInputSocket(EngineObject obj, IProperty<T> property)
    {
        Name = property.Name;
        Display = property.GetPropertyInfo()?.GetCustomAttribute<DisplayAttribute>();
        _property = property;
        property.Edited += (_, e) => RaiseEdited();
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
    }

    public void CopyFrom(IItemValue itemValue)
    {
        if (itemValue is not ItemValue<T> typed) return;
        if (!Connection.IsNull && _property.Expression is SocketExpression<T> exp)
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
            _property.Expression = Connection.IsNull ? null : new SocketExpression<T>();
        }
    }
}

[JsonConverter(typeof(SocketExpressionJsonConverter))]
public class SocketExpression<T> : IExpression<T>
{
    public T? Value { get; set; }

    public string ExpressionString => "[Socket Connected]";

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
internal class SocketExpressionJsonConverter : JsonConverter<IExpression>
{
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.GetGenericTypeDefinition() == typeof(SocketExpression<>);
    }

    public override IExpression? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var node = JsonNode.Parse(ref reader);
        if (node is not JsonObject) throw new JsonException();

        // typeToConvertはSocketExpression
        return Activator.CreateInstance(typeToConvert) as IExpression;
    }

    public override void Write(Utf8JsonWriter writer, IExpression value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteEndObject();
    }
}
