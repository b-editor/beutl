using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;

namespace BEditorNext.ProjectSystem;

public interface ISetter : IJsonSerializable, INotifyPropertyChanged
{
    public PropertyDefine Property { get; set; }

    public void SetProperty(Element element);
}

public class Setter<T> : ISetter
{
    private PropertyDefine<T>? _property;
    private T? _value;

    public Setter()
    {
    }

    public Setter(PropertyDefine<T> property)
    {
        Property = property;
        Value = property.GetDefaultValue();
    }

    public PropertyDefine<T> Property
    {
        get => _property ?? throw new Exception("The property is not set.");
        set
        {
            if (_property != value)
            {
                _property = value ?? throw new ArgumentNullException(nameof(value));
                OnPropertyChanged();
            }
        }
    }

    public T? Value
    {
        get => _value;
        set
        {
            if (!EqualityComparer<T>.Default.Equals(Value, _value))
            {
                _value = value;
                OnPropertyChanged();
            }
        }
    }

    PropertyDefine ISetter.Property
    {
        get => Property;
        set => Property = (PropertyDefine<T>)value;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public virtual void FromJson(JsonNode json)
    {
        if (json is JsonValue jv &&
            jv.TryGetValue(out T? value))
        {
            Value = value;
        }
    }

    public void SetProperty(Element element)
    {
        if (Value != null)
        {
            element.SetValue(Property, Value);
        }
    }

    public virtual JsonNode ToJson()
    {
        return JsonValue.Create(Value)!;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyname = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyname));
    }
}
