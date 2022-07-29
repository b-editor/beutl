using System.Text.Json.Nodes;

using BeUtl.Services;
using BeUtl.Services.Editors.Wrappers;
using BeUtl.Streaming;
using BeUtl.ViewModels.Editors;

using Reactive.Bindings;

namespace BeUtl.ViewModels.Tools;

public sealed class StylingOperatorViewModel : IDisposable
{
    private readonly IDisposable _disposable0;

    public StylingOperatorViewModel(StylingOperator model)
    {
        Model = model;
        _disposable0 = model.Style.Setters.ForEachItem(
            (idx, item) =>
            {
                Type type = typeof(StylingSetterWrapper<>);

                type = type.MakeGenericType(item.Property.PropertyType);
                var wrapper = (IWrappedProperty)Activator.CreateInstance(type, item)!;

                Properties.Insert(idx, PropertyEditorService.CreateEditorViewModel(wrapper));
            },
            (idx, _) =>
            {
                Properties[idx]?.Dispose();
                Properties.RemoveAt(idx);
            },
            () =>
            {
                foreach (BaseEditorViewModel? item in Properties.AsSpan())
                {
                    item?.Dispose();
                }
                Properties.Clear();
            });
    }

    public StylingOperator Model { get; }

    public ReactiveProperty<bool> IsExpanded { get; } = new(true);

    public CoreList<BaseEditorViewModel?> Properties { get; } = new();

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
        _disposable0.Dispose();
        foreach (BaseEditorViewModel? item in Properties.AsSpan())
        {
            item?.Dispose();
        }
    }
}
