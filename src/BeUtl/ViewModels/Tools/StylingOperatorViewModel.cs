using System.Collections.Specialized;
using System.Text.Json.Nodes;

using BeUtl.Framework;
using BeUtl.Services;
using BeUtl.Streaming;

using DynamicData;

using Reactive.Bindings;

namespace BeUtl.ViewModels.Tools;

public sealed class StylingOperatorViewModel : IDisposable
{
    public StylingOperatorViewModel(StreamOperator model)
    {
        Model = model;

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

    public StreamOperator Model { get; }

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
        Model.Properties.CollectionChanged -= Properties_CollectionChanged;
        foreach (IPropertyEditorContext? item in Properties.GetMarshal().Value)
        {
            item?.Dispose();
        }
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
                }

                props.RemoveMany(foundItems);
            }
        } while (foundItems != null && extension != null);
    }
}
