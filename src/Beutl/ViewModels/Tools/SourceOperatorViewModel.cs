using System.Collections.Specialized;
using System.Text.Json.Nodes;

using Beutl.Framework;
using Beutl.Services;
using Beutl.Operation;

using DynamicData;

using Reactive.Bindings;

namespace Beutl.ViewModels.Tools;

public sealed class SourceOperatorViewModel : IDisposable, IPropertyEditorContextVisitor, IProvideEditViewModel
{
    public SourceOperatorViewModel(SourceOperator model, EditViewModel editViewModel)
    {
        Model = model;
        EditViewModel = editViewModel;
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

    public EditViewModel EditViewModel { get; private set; }

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
                        context.ReadFromJson(node);
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
                JsonNode node = new JsonObject();
                item.WriteToJson(ref node);
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
        EditViewModel = null!;
    }

    private void Init()
    {
        List<CoreProperty> props = Model.Properties.Select(x => x.Property).ToList();
        Properties.EnsureCapacity(props.Count);
        CoreProperty[]? foundItems;
        PropertyEditorExtension? extension;

        do
        {
            (foundItems, extension) = PropertyEditorService.MatchProperty(props);
            if (foundItems != null && extension != null)
            {
                var tmp = new IAbstractProperty[foundItems.Length];
                for (int i = 0; i < foundItems.Length; i++)
                {
                    CoreProperty item = foundItems[i];
                    tmp[i] = Model.Properties.First(x => x.Property.Id == item.Id);
                }

                if (extension.TryCreateContext(tmp, out IPropertyEditorContext? context))
                {
                    Properties.Add(context);
                    context.Accept(this);
                }

                props.RemoveMany(foundItems);
            }
        } while (foundItems != null && extension != null);
    }

    public void Visit(IPropertyEditorContext context)
    {
    }
}
