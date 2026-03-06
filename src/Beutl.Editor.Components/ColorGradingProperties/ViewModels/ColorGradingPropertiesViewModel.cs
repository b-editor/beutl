using System.Text.Json.Nodes;
using Beutl.Editor.Services;
using Beutl.Graphics.Effects;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;

namespace Beutl.Editor.Components.ColorGradingProperties.ViewModels;

public class ColorGradingPropertiesViewModel : IPropertyEditorContext, IServiceProvider
{
    private readonly IReadOnlyList<IPropertyAdapter> _props;
    private IServiceProvider? _parentServices;
    private bool _editorsCreated;

    public ColorGradingPropertiesViewModel(IReadOnlyList<IPropertyAdapter> props)
    {
        _props = props;
    }

    public PropertyEditorExtension Extension => ColorGradingPropertiesExtension.Instance;

    public ReactivePropertySlim<bool> IsExpanded { get; } = new(false);

    public CoreList<IPropertyEditorContext> Properties { get; } = [];

    public ColorGrading? TryGetColorGrading()
    {
        if (_props.Count == 0) return null;
        return _props[0].GetEngineProperty()?.GetOwnerObject() as ColorGrading;
    }

    public object? GetService(Type serviceType)
    {
        return _parentServices?.GetService(serviceType);
    }

    private void CreateEditors()
    {
        if (_editorsCreated) return;
        _editorsCreated = true;

        var factory = _parentServices?.GetService<IPropertyEditorFactory>();
        if (factory == null) return;

        Properties.EnsureCapacity(_props.Count);
        foreach (IPropertyAdapter prop in _props)
        {
            var ctx = factory.CreateEditor(prop);
            if (ctx != null)
            {
                Properties.Add(ctx);
            }
        }
    }

    private void AcceptChildren()
    {
        var visitor = new Visitor(this);
        foreach (IPropertyEditorContext item in Properties)
        {
            item.Accept(visitor);
        }
    }

    public void Accept(IPropertyEditorContextVisitor visitor)
    {
        visitor.Visit(this);
        if (visitor is IServiceProvider serviceProvider)
        {
            _parentServices = serviceProvider;
        }

        if (visitor is IServiceProvider)
        {
            CreateEditors();
            AcceptChildren();
        }
    }

    public void ReadFromJson(JsonObject json)
    {
        if (json.TryGetPropertyValue(nameof(IsExpanded), out JsonNode? isExpandedNode)
            && isExpandedNode is JsonValue isExpanded)
        {
            IsExpanded.Value = (bool)isExpanded;
        }

        if (json.TryGetPropertyValue(nameof(Properties), out JsonNode? propsNode)
            && propsNode is JsonArray propsArray)
        {
            foreach ((JsonNode? node, IPropertyEditorContext? context) in propsArray.Zip(Properties))
            {
                if (node != null)
                {
                    context.ReadFromJson(node.AsObject());
                }
            }
        }
    }

    public void WriteToJson(JsonObject json)
    {
        try
        {
            json[nameof(IsExpanded)] = IsExpanded.Value;
            var array = new JsonArray();

            foreach (IPropertyEditorContext? item in Properties.GetMarshal().Value)
            {
                var node = new JsonObject();
                item.WriteToJson(node);
                array.Add(node);
            }

            json[nameof(Properties)] = array;
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        IsExpanded.Dispose();
        foreach (IPropertyEditorContext item in Properties)
        {
            item.Dispose();
        }
    }

    private sealed record Visitor(ColorGradingPropertiesViewModel Obj) : IServiceProvider, IPropertyEditorContextVisitor
    {
        public object? GetService(Type serviceType)
        {
            return Obj._parentServices?.GetService(serviceType);
        }

        public void Visit(IPropertyEditorContext context)
        {
        }
    }
}
