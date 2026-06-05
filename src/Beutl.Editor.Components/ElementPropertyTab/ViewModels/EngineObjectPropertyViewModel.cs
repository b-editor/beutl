using System.Text.Json.Nodes;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.ProjectSystem;
using Beutl.PropertyAdapters;
using Beutl.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;

namespace Beutl.Editor.Components.ElementPropertyTab.ViewModels;

public sealed class EngineObjectPropertyViewModel : IDisposable, IPropertyEditorContextVisitor, IServiceProvider, IFallbackObjectViewModel
{
    private ElementPropertyTabViewModel _parent;

    public EngineObjectPropertyViewModel(EngineObject model, ElementPropertyTabViewModel parent)
    {
        Model = model;
        _parent = parent;
        IsEnabled = model.GetObservable(EngineObject.IsEnabledProperty)
            .ToReactiveProperty();
        IsEnabled.Skip(1).Subscribe(v =>
        {
            IElementObjectService? service = this.GetService<IElementObjectService>();
            service?.SetEnabled(Model, v);
        });

        Init();

        IsFallback = Observable.ReturnThenNever(model is IFallback)
            .ToReadOnlyReactivePropertySlim();

        ActualTypeName = Observable.ReturnThenNever(FallbackHelper.GetTypeName(model))
            .ToReadOnlyReactivePropertySlim()!;

        FallbackMessage = Observable.ReturnThenNever(FallbackHelper.GetFallbackMessage(model))
            .ToReadOnlyReactivePropertySlim()!;
    }

    public EngineObject Model { get; private set; }

    public ReactiveProperty<bool> IsExpanded { get; } = new(true);

    public ReactiveProperty<bool> IsEnabled { get; }

    public CoreList<IPropertyEditorContext?> Properties { get; } = [];

    public IReadOnlyReactiveProperty<bool> IsFallback { get; }

    public IReadOnlyReactiveProperty<string> ActualTypeName { get; }

    public IReadOnlyReactiveProperty<string> FallbackMessage { get; }

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
        var factory = this.GetRequiredService<IPropertyEditorFactory>();
        var adapters = PropertyAdapterFactory.CreateAdapters(Model);
        var contexts = factory.CreatePropertyEditorContexts(adapters, this);
        Properties.AddRange(contexts);
    }

    public void Visit(IPropertyEditorContext context)
    {
    }

    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(EngineObject))
            return Model;

        return _parent.GetService(serviceType);
    }

    public IObservable<string?> GetJsonString()
    {
        if (Model is FallbackEngineObject { Json: JsonObject json })
        {
            return Observable.ReturnThenNever(json.ToJsonString(JsonHelper.SerializerOptions));
        }

        return Observable.ReturnThenNever<string?>(null);
    }

    public void SetJsonString(string? str)
    {
        if (Model.HierarchicalParent is not Element element) return;

        int index = element.Objects.IndexOf(Model);
        if (index < 0) return;

        string message = MessageStrings.InvalidJson;
        if (str is null) throw new Exception(message);

        ObjectPasteOutcome outcome = this.GetRequiredService<IElementObjectService>()
            .PasteOver(element, index, str);
        if (outcome != ObjectPasteOutcome.Pasted)
        {
            throw new Exception(message);
        }
    }
}
