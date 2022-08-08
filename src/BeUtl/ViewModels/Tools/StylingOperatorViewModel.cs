using System.Collections.Specialized;
using System.Text.Json.Nodes;

using BeUtl.Framework;
using BeUtl.Services;
using BeUtl.Services.Editors.Wrappers;
using BeUtl.Streaming;

using DynamicData;

using Reactive.Bindings;

namespace BeUtl.ViewModels.Tools;

public sealed class StylingOperatorViewModel : IDisposable
{
    public StylingOperatorViewModel(StylingOperator model)
    {
        Model = model;

        Init();

        model.Style.Setters.CollectionChanged += Setters_CollectionChanged;
    }

    private void Setters_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (IPropertyEditorContext? item in Properties.GetMarshal().Value)
        {
            item?.Dispose();
        }
        Properties.Clear();

        Init();
    }

    public StylingOperator Model { get; }

    public ReactiveProperty<bool> IsExpanded { get; } = new(true);

    public CoreList<IPropertyEditorContext?> Properties { get; } = new();

    public void RestoreState(JsonNode json)
    {
        try
        {
            IsExpanded.Value = (bool?)json["is-expanded"] ?? true;
        }
        catch
        {
        }
    }

    public JsonNode SaveState()
    {
        return new JsonObject
        {
            ["is-expanded"] = IsExpanded.Value
        };
    }

    public void Dispose()
    {
        Model.Style.Setters.CollectionChanged -= Setters_CollectionChanged;
        foreach (IPropertyEditorContext? item in Properties.GetMarshal().Value)
        {
            item?.Dispose();
        }
    }

    private void Init()
    {
        Type objType = Model.Style.TargetType;
        Type wrapperType = typeof(StylingSetterClientImpl<>);

        List<CoreProperty> props = Model.Style.Setters.Select(x => x.Property).ToList();
        Properties.EnsureCapacity(props.Count);
        CoreProperty[]? foundItems;
        PropertyEditorExtension? extension;

        do
        {
            (foundItems, extension) = PropertyEditorService.MatchProperty(props);
            if (foundItems != null && extension != null)
            {
                int index = 0;
                var tmp = new IAbstractProperty[foundItems.Length];
                foreach (CoreProperty item in foundItems)
                {
                    CorePropertyMetadata metadata = item.GetMetadata<CorePropertyMetadata>(objType);
                    Type wrapperGType = wrapperType.MakeGenericType(item.PropertyType);
                    tmp[index] = (IAbstractProperty)Activator.CreateInstance(
                        wrapperGType,
                        Model.Style.Setters.First(x => x.Property.Id == item.Id))!;

                    index++;
                }

                if (extension.TryCreateContext(tmp, out IPropertyEditorContext? context))
                {
                    Properties.Add(context);
                }

                props.RemoveMany(foundItems);
            }
        } while (foundItems != null && extension != null);
    }
}
