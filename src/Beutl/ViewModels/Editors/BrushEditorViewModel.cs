using System.Collections.Immutable;
using System.Text.Json.Nodes;

using Beutl.Animation;
using Beutl.Media;
using Beutl.Media.Immutable;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;

using ReactiveUI;

namespace Beutl.ViewModels.Editors;

public sealed class BrushEditorViewModel : BaseEditorViewModel
{
    private IDisposable? _revoker;

    public BrushEditorViewModel(IAbstractProperty property)
        : base(property)
    {
        Value = property.GetObservable()
            .Select(x => x as IBrush)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        AvaloniaBrush = new ReactiveProperty<Avalonia.Media.Brush?>();
        Value.Subscribe(v =>
        {
            _revoker?.Dispose();
            _revoker = null;
            (AvaloniaBrush.Value, _revoker) = v.ToAvaBrushSync();
        });

        ChildContext = Value.Select(v => v as ICoreObject)
            .Select(x => x != null ? new PropertiesEditorViewModel(x, m => m.Browsable) : null)
            .Do(AcceptChildren)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        IsSolid = Value.Select(v => v is ISolidColorBrush)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        IsLinearGradient = Value.Select(v => v is ILinearGradientBrush)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        IsConicGradient = Value.Select(v => v is IConicGradientBrush)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        IsRadialGradient = Value.Select(v => v is IRadialGradientBrush)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        IsPerlinNoise = Value.Select(v => v is IPerlinNoiseBrush)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        Value.CombineWithPrevious()
            .Select(v => v.OldValue as IAnimatable)
            .WhereNotNull()
            .Subscribe(v => this.GetService<ISupportCloseAnimation>()?.Close(v))
            .DisposeWith(Disposables);
    }

    private void AcceptChildren(PropertiesEditorViewModel? obj)
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

    public ReadOnlyReactivePropertySlim<IBrush?> Value { get; }

    public ReactiveProperty<Avalonia.Media.Brush?> AvaloniaBrush { get; }

    public ReadOnlyReactivePropertySlim<PropertiesEditorViewModel?> ChildContext { get; }

    public ReadOnlyReactivePropertySlim<bool> IsSolid { get; }

    public ReadOnlyReactivePropertySlim<bool> IsLinearGradient { get; }

    public ReadOnlyReactivePropertySlim<bool> IsConicGradient { get; }

    public ReadOnlyReactivePropertySlim<bool> IsRadialGradient { get; }

    public ReadOnlyReactivePropertySlim<bool> IsPerlinNoise { get; }

    public ReactivePropertySlim<bool> IsExpanded { get; } = new();

    public override void Reset()
    {
        if (GetDefaultValue() is { } defaultValue)
        {
            SetValue(Value.Value, (IBrush?)defaultValue);
        }
    }

    public void SetValue(IBrush? oldValue, IBrush? newValue)
    {
        if (!EqualityComparer<IBrush>.Default.Equals(oldValue, newValue))
        {
            CommandRecorder recorder = this.GetRequiredService<CommandRecorder>();
            IAbstractProperty prop = WrappedProperty;

            RecordableCommands.Create(GetStorables())
                .OnDo(() => prop.SetValue(newValue))
                .OnUndo(() => prop.SetValue(oldValue))
                .ToCommand()
                .DoAndRecord(recorder);
        }
    }

    public void InsertGradientStop(int index, GradientStop item)
    {
        if (Value.Value is GradientBrush { GradientStops: { } list })
        {
            CommandRecorder recorder = this.GetRequiredService<CommandRecorder>();
            list.BeginRecord<GradientStop>()
                .Insert(index, item)
                .ToCommand(GetStorables())
                .DoAndRecord(recorder);
        }
    }

    public void RemoveGradientStop(int index)
    {
        if (Value.Value is GradientBrush { GradientStops: { } list })
        {
            CommandRecorder recorder = this.GetRequiredService<CommandRecorder>();
            list.BeginRecord<GradientStop>()
                .RemoveAt(index)
                .ToCommand(GetStorables())
                .DoAndRecord(recorder);
        }
    }

    public void ConfirmeGradientStop(
        int oldIndex, int newIndex,
        ImmutableGradientStop oldObject, GradientStop obj)
    {
        CommandRecorder recorder = this.GetRequiredService<CommandRecorder>();
        if (Value.Value is GradientBrush { GradientStops: { } list })
        {
            IRecordableCommand? move = oldIndex == newIndex ? null : list.BeginRecord<GradientStop>()
                .Move(oldIndex, newIndex)
                .ToCommand([]);

            IRecordableCommand? offset = obj.Offset != oldObject.Offset
                ? RecordableCommands.Edit(obj, GradientStop.OffsetProperty, obj.Offset, oldObject.Offset)
                : null;
            IRecordableCommand? color = obj.Color != oldObject.Color
                ? RecordableCommands.Edit(obj, GradientStop.ColorProperty, obj.Color, oldObject.Color)
                : null;

            move.Append(offset)
                .Append(color)
                .WithStoables(GetStorables())
                .DoAndRecord(recorder);
        }
    }

    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        if (visitor is IServiceProvider)
        {
            AcceptChildren(ChildContext.Value);
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
            ChildContext.Value?.ReadFromJson(json);
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
            ChildContext.Value?.WriteToJson(json);
        }
        catch
        {
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        ChildContext.Value?.Dispose();
        _revoker?.Dispose();
    }

    private sealed record Visitor(BrushEditorViewModel Obj) : IServiceProvider, IPropertyEditorContextVisitor
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
