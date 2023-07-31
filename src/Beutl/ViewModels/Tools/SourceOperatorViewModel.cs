using System.Collections.Specialized;
using System.Text.Json.Nodes;

using Beutl.Framework;
using Beutl.Services;
using Beutl.Operation;

using DynamicData;

using Reactive.Bindings;
using Beutl.ViewModels.Editors;
using Microsoft.CodeAnalysis;

namespace Beutl.ViewModels.Tools;

public sealed class SourceOperatorViewModel : IDisposable, IPropertyEditorContextVisitor, IServiceProvider
{
    private SourceOperatorsTabViewModel _parent;

    public SourceOperatorViewModel(SourceOperator model, SourceOperatorsTabViewModel parent)
    {
        Model = model;
        _parent = parent;
        IsEnabled = model.GetObservable(SourceOperator.IsEnabledProperty)
            .ToReactiveProperty();
        IsEnabled.Subscribe(v => Model.IsEnabled = v);

        Init();

        model.Properties.CollectionChanged += Properties_CollectionChanged;
    }

    private void Properties_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (IPropertyEditorContext? item in Properties.GetMarshal().Value)
        {
            item?.Dispose();
        }
        Properties.Clear();

        Init();
    }

    public SourceOperator Model { get; private set; }

    public ReactiveProperty<bool> IsExpanded { get; } = new(true);

    public ReactiveProperty<bool> IsEnabled { get; }

    public CoreList<IPropertyEditorContext?> Properties { get; } = new();

    public void RestoreState(JsonNode json)
    {
        if (json is JsonObject obj)
        {
            if (obj.TryGetPropertyValue("is-expanded", out JsonNode? isExpandedNode)
                && isExpandedNode is JsonValue isExpandedValue
                && isExpandedValue.TryGetValue(out bool isExpanded))
            {
                IsExpanded.Value = isExpanded;
            }

            if (obj.TryGetPropertyValue("properties", out JsonNode? propsNode)
                && propsNode is JsonArray propsArray)
            {
                foreach ((JsonNode? node, IPropertyEditorContext? context) in propsArray.Zip(Properties))
                {
                    if (context != null && node != null)
                    {
                        context.ReadFromJson(node.AsObject());
                    }
                }
            }
        }
    }

    public JsonNode SaveState()
    {
        var array = new JsonArray();

        foreach (IPropertyEditorContext? item in Properties.GetMarshal().Value)
        {
            if (item == null)
            {
                array.Add(null);
            }
            else
            {
                var node = new JsonObject();
                item.WriteToJson(node);
                array.Add(node);
            }
        }

        return new JsonObject
        {
            ["is-expanded"] = IsExpanded.Value,
            ["properties"] = array
        };
    }

    public void Dispose()
    {
        Model.Properties.CollectionChanged -= Properties_CollectionChanged;
        foreach (IPropertyEditorContext? item in Properties.GetMarshal().Value)
        {
            item?.Dispose();
        }
        Properties.Clear();
        IsEnabled.Dispose();

        Model = null!;
        _parent = null!;
    }

    private void Init()
    {
        List<IAbstractProperty> props = Model.Properties.ToList();
        var tempItems = new List<IPropertyEditorContext?>(props.Count);
        IAbstractProperty[]? foundItems;
        PropertyEditorExtension? extension;

        do
        {
            (foundItems, extension) = PropertyEditorService.MatchProperty(props);
            if (foundItems != null && extension != null)
            {
                if (extension.TryCreateContext(foundItems, out IPropertyEditorContext? context))
                {
                    tempItems.Add(context);
                    context.Accept(this);
                }

                props.RemoveMany(foundItems);
            }
        } while (foundItems != null && extension != null);

        foreach ((string? Key, IPropertyEditorContext?[] Value) group in tempItems.GroupBy(x =>
            {
                if (x is BaseEditorViewModel { WrappedProperty: { } abProperty }
                    && abProperty.GetCoreProperty() is { } coreProperty
                    && coreProperty.TryGetMetadata<CorePropertyMetadata>(abProperty.ImplementedType, out var metadata))
                {
                    return metadata.DisplayAttribute?.GetGroupName();
                }
                else
                {
                    return null;
                }
            })
            .Select(x => (x.Key, x.ToArray())))
        {
            if (group.Key != null)
            {
                IPropertyEditorContext?[] array = group.Value;
                if (array.Length >= 1)
                {
                    int index = tempItems.IndexOf(array[0]);
                    tempItems.RemoveMany(array);
                    tempItems.Insert(index, new PropertyEditorGroupContext(array, group.Key));
                }
            }
        }

        Properties.AddRange(tempItems);
    }

    public void Visit(IPropertyEditorContext context)
    {
    }

    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(SourceOperator))
            return Model;

        return _parent.GetService(serviceType);
    }
}
