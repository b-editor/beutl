using System.Text.Json.Nodes;
using Beutl.Editor.Components.Helpers;
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
        var visitor = new ChildVisitor(this);
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

        NestedEditorContextHelper.ReadEditorArray(json, nameof(Properties), Properties);
    }

    public void WriteToJson(JsonObject json)
    {
        try
        {
            json[nameof(IsExpanded)] = IsExpanded.Value;
            NestedEditorContextHelper.WriteEditorArray(json, nameof(Properties), Properties);
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
}
