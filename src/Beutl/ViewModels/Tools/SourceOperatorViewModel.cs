using System.Collections.Specialized;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using Beutl.Editor;
using Beutl.Helpers;
using Beutl.Operation;
using Beutl.Serialization;
using Beutl.Services;
using Beutl.ViewModels.Editors;
using DynamicData;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;

namespace Beutl.ViewModels.Tools;

public sealed class SourceOperatorViewModel : IDisposable, IPropertyEditorContextVisitor, IServiceProvider, IUnknownObjectViewModel
{
    private SourceOperatorsTabViewModel _parent;

    public SourceOperatorViewModel(SourceOperator model, SourceOperatorsTabViewModel parent)
    {
        Model = model;
        _parent = parent;
        IsEnabled = model.GetObservable(SourceOperator.IsEnabledProperty)
            .ToReactiveProperty();
        IsEnabled.Skip(1).Subscribe(v =>
        {
            HistoryManager? history = this.GetService<HistoryManager>();
            if (history != null)
            {
                Model.IsEnabled = v;
                history.Commit(CommandNames.ChangeSourceOperatorEnabled);
            }
        });

        Init();

        model.Properties.CollectionChanged += Properties_CollectionChanged;

        IsDummy = Observable.ReturnThenNever(model is IDummy)
            .ToReadOnlyReactivePropertySlim();

        ActualTypeName = Observable.ReturnThenNever(DummyHelper.GetTypeName(model))
            .ToReadOnlyReactivePropertySlim()!;
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

    public CoreList<IPropertyEditorContext?> Properties { get; } = [];

    public IReadOnlyReactiveProperty<bool> IsDummy { get; }

    public IReadOnlyReactiveProperty<string> ActualTypeName { get; }

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

        return new JsonObject { ["is-expanded"] = IsExpanded.Value, ["properties"] = array };
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
        List<IPropertyAdapter> props = [.. Model.Properties];
        var tempItems = new List<IPropertyEditorContext?>(props.Count);
        IPropertyAdapter[]? foundItems;
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
                         if (x is BaseEditorViewModel { PropertyAdapter: { } adapter })
                         {
                             return (adapter.GetAttributes().FirstOrDefault(i => i is DisplayAttribute) as DisplayAttribute)
                                 ?.GetGroupName();
                         }
                         else
                         {
                             return null;
                         }
                     })
                     .Select(x => (x.Key, x.ToArray()))
                     .ToArray())
        {
            if (group.Key != null)
            {
                IPropertyEditorContext?[] array = group.Value;
                if (array.Length >= 1)
                {
                    int index = tempItems.IndexOf(array[0]);
                    tempItems.RemoveMany(array);
                    tempItems.Insert(index, new PropertyEditorGroupContext(array, group.Key, index == 0));
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

    public IObservable<string?> GetJsonString()
    {
        if (Model is DummySourceOperator { Json: JsonObject json })
        {
            return Observable.ReturnThenNever(json.ToJsonString(JsonHelper.SerializerOptions));
        }

        return Observable.ReturnThenNever<string?>(null);
    }

    public void SetJsonString(string? str)
    {
        if (Model.HierarchicalParent is not SourceOperation sourceOperation) return;

        int index = sourceOperation.Children.IndexOf(Model);
        if (index < 0) return;

        string message = Strings.InvalidJson;
        _ = str ?? throw new Exception(message);
        JsonObject json = (JsonNode.Parse(str) as JsonObject) ?? throw new Exception(message);

        Type? type = json.GetDiscriminator();
        SourceOperator? @operator = null;
        if (type?.IsAssignableTo(typeof(SourceOperator)) ?? false)
        {
            @operator = Activator.CreateInstance(type) as SourceOperator;
        }

        if (@operator == null) throw new Exception(message);

        CoreSerializer.PopulateFromJsonObject(@operator, type!, json);

        HistoryManager history = this.GetRequiredService<HistoryManager>();

        sourceOperation.Children[index] = @operator;
        history.Commit(CommandNames.PasteSourceOperator);
    }
}
