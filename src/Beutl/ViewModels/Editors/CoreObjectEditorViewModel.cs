using System.Text.Json.Nodes;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.ViewModels.Editors;

public interface ICoreObjectEditorViewModel
{
    string Header { get; }

    ReadOnlyReactivePropertySlim<PropertiesEditorViewModel?> Properties { get; }

    ReactivePropertySlim<bool> IsExpanded { get; }

    ReadOnlyReactivePropertySlim<bool> CanEdit { get; }

    ReadOnlyReactivePropertySlim<bool> IsNull { get; }

    ReadOnlyReactivePropertySlim<bool> IsNotSetAndCanWrite { get; }

    bool CanWrite { get; }
}

public sealed class CoreObjectEditorViewModel<T> : BaseEditorViewModel<T>, ICoreObjectEditorViewModel
    where T : ICoreObject
{
    public CoreObjectEditorViewModel(IPropertyAdapter<T> property)
        : base(property)
    {
        CanWrite = !property.IsReadOnly;

        IsNull = property.GetObservable()
            .Select(x => x == null)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        IsNotSetAndCanWrite = IsNull.Select(x => x && CanWrite)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        Value = property.GetObservable()
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        Properties = Value
            .Select(x => x != null ? new PropertiesEditorViewModel(x) : null)
            .DisposePreviousValue()
            .Do(AcceptProperties)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);
    }

    public ReadOnlyReactivePropertySlim<T?> Value { get; }

    public ReadOnlyReactivePropertySlim<PropertiesEditorViewModel?> Properties { get; }

    public ReactivePropertySlim<bool> IsExpanded { get; } = new();

    public ReadOnlyReactivePropertySlim<bool> IsNull { get; }

    public ReadOnlyReactivePropertySlim<bool> IsNotSetAndCanWrite { get; }

    public bool CanWrite { get; }

    private void AcceptProperties(PropertiesEditorViewModel? obj)
    {
        if (obj != null)
        {
            var visitor = new Visitor(this);
            foreach (IPropertyEditorContext item in obj.Properties)
            {
                item.Accept(visitor);
            }
        }
    }

    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        if (visitor is IServiceProvider)
        {
            AcceptProperties(Properties.Value);
        }
    }

    public override void ReadFromJson(JsonObject json)
    {
        base.ReadFromJson(json);
        try
        {
            if (json.TryGetPropertyValue(nameof(IsExpanded), out JsonNode? isExpandedNode)
                && isExpandedNode is JsonValue isExpanded)
            {
                IsExpanded.Value = (bool)isExpanded;
            }

            Properties.Value?.ReadFromJson(json);
        }
        catch
        {
        }
    }

    public override void WriteToJson(JsonObject json)
    {
        base.WriteToJson(json);
        try
        {
            json[nameof(IsExpanded)] = IsExpanded.Value;
            Properties.Value?.WriteToJson(json);
        }
        catch
        {
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Properties.Value?.Dispose();
    }

    private sealed record Visitor(CoreObjectEditorViewModel<T> Obj) : IServiceProvider, IPropertyEditorContextVisitor
    {
        public object? GetService(Type serviceType)
        {
            return Obj.GetService(serviceType);
        }

        public void Visit(IPropertyEditorContext context)
        {
        }
    }
}
