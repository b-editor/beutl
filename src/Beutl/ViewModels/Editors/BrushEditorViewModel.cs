using System.Text.Json.Nodes;
using Beutl.Animation;
using Beutl.Graphics;
using Beutl.Media;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;

namespace Beutl.ViewModels.Editors;

public sealed class BrushEditorViewModel : BaseEditorViewModel
{
    private IDisposable? _revoker;
    private Action? _update;

    public BrushEditorViewModel(IPropertyAdapter<Brush?> property)
        : base(property)
    {
        Value = property.GetObservable()
            .ToReadOnlyReactiveProperty()
            .DisposeWith(Disposables);

        AvaloniaBrush = new ReactiveProperty<Avalonia.Media.Brush?>();
        Value.Subscribe(v =>
        {
            _revoker?.Dispose();
            _revoker = null;
            (AvaloniaBrush.Value, _revoker, _update) = v.ToAvaBrushSync(CurrentTime);
        });

        ChildContext = Value.Select(v => v as ICoreObject)
            .Select(x => x != null ? new PropertiesEditorViewModel(x) : null)
            .Do(AcceptChildren)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        IsSolid = Value.Select(v => v is SolidColorBrush)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        IsLinearGradient = Value.Select(v => v is LinearGradientBrush)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        IsConicGradient = Value.Select(v => v is ConicGradientBrush)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        IsRadialGradient = Value.Select(v => v is RadialGradientBrush)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        IsPerlinNoise = Value.Select(v => v is PerlinNoiseBrush)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        IsDrawable = Value.Select(v => v is DrawableBrush)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        Value.CombineWithPrevious()
            .Select(v => v.OldValue)
            .Where(v => v != null)
            .Subscribe(v => this.GetService<ISupportCloseAnimation>()?.Close(v!))
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

    public ReadOnlyReactiveProperty<Brush?> Value { get; }

    public ReactiveProperty<Avalonia.Media.Brush?> AvaloniaBrush { get; }

    public ReadOnlyReactivePropertySlim<PropertiesEditorViewModel?> ChildContext { get; }

    public ReadOnlyReactivePropertySlim<bool> IsSolid { get; }

    public ReadOnlyReactivePropertySlim<bool> IsLinearGradient { get; }

    public ReadOnlyReactivePropertySlim<bool> IsConicGradient { get; }

    public ReadOnlyReactivePropertySlim<bool> IsRadialGradient { get; }

    public ReadOnlyReactivePropertySlim<bool> IsPerlinNoise { get; }

    public ReadOnlyReactivePropertySlim<bool> IsDrawable { get; }

    public ReactivePropertySlim<bool> IsExpanded { get; } = new();

    public void UpdateBrushPreview()
    {
        _update?.Invoke();
    }

    public override void Reset()
    {
        if (GetDefaultValue() is { } defaultValue)
        {
            SetValue(Value.Value, (Brush?)defaultValue);
        }
    }

    public void SetValue(Brush? oldValue, Brush? newValue)
    {
        if (!EqualityComparer<Brush>.Default.Equals(oldValue, newValue))
        {
            CommandRecorder recorder = this.GetRequiredService<CommandRecorder>();

            RecordableCommands.Edit((IPropertyAdapter<Brush>)PropertyAdapter, newValue, oldValue)
                .WithStoables(GetStorables())
                .DoAndRecord(recorder);
        }
    }

    public void SetColor(Color oldValue, Color newValue)
    {
        if (Value.Value is SolidColorBrush solid)
        {
            CommandRecorder recorder = this.GetRequiredService<CommandRecorder>();

            // TODO: Colorプロパティにアニメーションが適用されている時の対応
            RecordableCommands.Edit(solid.Color, newValue, oldValue)
                .WithStoables(GetStorables())
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
        GradientStop.Resource oldObject, GradientStop obj)
    {
        CommandRecorder recorder = this.GetRequiredService<CommandRecorder>();
        if (Value.Value is GradientBrush { GradientStops: { } list })
        {
            IRecordableCommand? move = oldIndex == newIndex
                ? null
                : list.BeginRecord<GradientStop>()
                    .Move(oldIndex, newIndex)
                    .ToCommand([]);

            IRecordableCommand? offset = obj.Offset.CurrentValue != oldObject.Offset
                ? RecordableCommands.Edit(obj.Offset, obj.Offset.CurrentValue, oldObject.Offset)
                : null;
            IRecordableCommand? color = obj.Color.CurrentValue != oldObject.Color
                ? RecordableCommands.Edit(obj.Color, obj.Color.CurrentValue, oldObject.Color)
                : null;

            move.Append(offset)
                .Append(color)
                .WithStoables(GetStorables())
                .DoAndRecord(recorder);
        }
    }

    public void ChangeDrawableType(Type type)
    {
        if (Value.Value is Media.DrawableBrush drawable)
        {
            if (Activator.CreateInstance(type) is Drawable instance)
            {
                CommandRecorder recorder = this.GetRequiredService<CommandRecorder>();
                RecordableCommands.Edit(drawable.Drawable, instance)
                    .WithStoables(GetStorables())
                    .DoAndRecord(recorder);
            }
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
