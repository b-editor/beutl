using System.Text.Json.Nodes;
using Avalonia.Input;
using Beutl.Composition;
using Beutl.Editor.Components.Helpers;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;

namespace Beutl.ViewModels.Editors;

public sealed class BrushEditorViewModel : BaseEditorViewModel, IFallbackObjectViewModel
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

        CanCopy = Value.Select(v => v is Brush and not FallbackBrush)
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

        IsFallback = Value.Select(v => v is IFallback)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        ActualTypeName = Value.Select(FallbackHelper.GetTypeName)
            .ToReadOnlyReactivePropertySlim(Strings.Unknown)
            .DisposeWith(Disposables);

        FallbackMessage = Value.Select(FallbackHelper.GetFallbackMessage)
            .ToReadOnlyReactivePropertySlim(MessageStrings.RestoreFailedTypeNotFound)
            .DisposeWith(Disposables);

        var expressionObservable = Value
            .Select(v => v switch
            {
                IPresenter<Brush> presenter => presenter.Target.SubscribeExpressionChange()
                    .Select(exp => (presenter, exp))!,
                _ => Observable.ReturnThenNever(
                    ((IPresenter<Brush>?)null, (IExpression<Brush?>?)null))
            })
            .Switch();
        IsPresenter = expressionObservable
            .Select(t => t is { Item1: not null, Item2: ReferenceExpression<Brush> or null })
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        CurrentTargetName = expressionObservable
            .Select(t => t.Item2 is ReferenceExpression<Brush>
                ? t.Item1?.Target.GetValue(CompositionContext.Default)
                : null)
            .Select(fe => fe != null ? CoreObjectHelper.GetDisplayName(fe) : MessageStrings.PropertyUnset)
            .ToReadOnlyReactivePropertySlim()
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

    public override IReadOnlyReactiveProperty<bool> CanCopy { get; }

    public override IReadOnlyReactiveProperty<bool> CanSaveAsTemplate => CanCopy;

    protected override Type? TemplateBaseType => typeof(Brush);

    protected override DataFormat<string>? PasteFormat => BeutlDataFormats.Brush;

    public ReadOnlyReactivePropertySlim<bool> IsSolid { get; }

    public ReadOnlyReactivePropertySlim<bool> IsLinearGradient { get; }

    public ReadOnlyReactivePropertySlim<bool> IsConicGradient { get; }

    public ReadOnlyReactivePropertySlim<bool> IsRadialGradient { get; }

    public ReadOnlyReactivePropertySlim<bool> IsPerlinNoise { get; }

    public ReadOnlyReactivePropertySlim<bool> IsDrawable { get; }

    public ReadOnlyReactivePropertySlim<bool> IsPresenter { get; }

    public ReadOnlyReactivePropertySlim<string?> CurrentTargetName { get; }

    public ReactivePropertySlim<bool> IsExpanded { get; } = new();

    public IReadOnlyReactiveProperty<bool> IsFallback { get; }

    public IReadOnlyReactiveProperty<string> ActualTypeName { get; }

    public IReadOnlyReactiveProperty<string> FallbackMessage { get; }

    public IObservable<string?> GetJsonString()
    {
        return Value.Select(v =>
        {
            if (v is FallbackBrush { Json: JsonObject json })
            {
                return json.ToJsonString(JsonHelper.SerializerOptions);
            }

            return null;
        });
    }

    public void SetJsonString(string? str)
    {
        string message = MessageStrings.InvalidJson;
        _ = str ?? throw new Exception(message);
        JsonObject json = (JsonNode.Parse(str) as JsonObject) ?? throw new Exception(message);

        Type? type = json.GetDiscriminator();
        Brush? instance = null;
        if (type?.IsAssignableTo(typeof(Brush)) ?? false)
        {
            instance = Activator.CreateInstance(type) as Brush;
        }

        if (instance == null) throw new Exception(message);

        CoreSerializer.PopulateFromJsonObject(instance, type!, json);

        SetValue(Value.Value, instance);
    }

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
            PropertyAdapter.SetValue(newValue);
            Commit();
        }
    }

    protected override ICoreSerializable? GetCopyTarget()
        => Value.Value is Brush brush and not FallbackBrush ? brush : null;

    protected override ICoreSerializable? GetTemplateTarget() => GetCopyTarget();

    public override bool ApplyTemplate(ObjectTemplateItem template)
    {
        if (template.CreateInstance() is not Brush instance) return false;
        IsExpanded.Value = true;
        PropertyAdapter.SetValue(instance);
        Commit(CommandNames.ApplyTemplate);
        return true;
    }

    public override bool TryPasteJson(string json)
    {
        if (!CoreObjectClipboard.TryDeserializeJson<Brush>(json, out var pasted)) return false;

        IsExpanded.Value = true;
        PropertyAdapter.SetValue(pasted);
        Commit(CommandNames.PasteObject);
        return true;
    }

    public void SetColor(Color oldValue, Color newValue)
    {
        if (Value.Value is SolidColorBrush solid)
        {
            solid.Color.CurrentValue = newValue;
            Commit();
        }
    }

    public void InsertGradientStop(int index, GradientStop item)
    {
        if (Value.Value is GradientBrush { GradientStops: { } list })
        {
            list.Insert(index, item);
            Commit();
        }
    }

    public void RemoveGradientStop(int index)
    {
        if (Value.Value is GradientBrush { GradientStops: { } list })
        {
            list.RemoveAt(index);
            Commit();
        }
    }

    public void ConfirmeGradientStop(
        int oldIndex, int newIndex,
        GradientStop.Resource oldObject, GradientStop obj)
    {
        if (Value.Value is GradientBrush { GradientStops: { } list })
        {
            if (oldIndex != newIndex)
                list.Move(oldIndex, newIndex);

            Commit();
        }
    }

    public void ChangeDrawableType(Type type)
    {
        if (Value.Value is Media.DrawableBrush drawable)
        {
            if (Activator.CreateInstance(type) is Drawable instance)
            {
                drawable.Drawable.CurrentValue = instance;
                Commit();
            }
        }
    }

    public void SetTarget(Brush? target)
    {
        if (Value.Value is IPresenter<Brush> presenter)
        {
            if (target != null)
            {
                presenter.Target.Expression = Expression.CreateReference<Brush>(target.Id);
            }
            else
            {
                presenter.Target.Expression = null;
                presenter.Target.CurrentValue = null;
            }
            Commit();
        }
    }

    public void SetDrawableTarget(Drawable? target)
    {
        if (Value.Value is not DrawableBrush drawableBrush)
        {
            drawableBrush = new DrawableBrush();
            PropertyAdapter.SetValue(drawableBrush);
        }

        if (drawableBrush.Drawable.CurrentValue is not DrawablePresenter presenter)
        {
            presenter = new DrawablePresenter();
            drawableBrush.Drawable.CurrentValue = presenter;
        }

        if (target != null)
        {
            presenter.Target.Expression = Expression.CreateReference<Drawable>(target.Id);
        }
        else
        {
            presenter.Target.Expression = null;
            presenter.Target.CurrentValue = null;
        }

        Commit();
    }

    public IReadOnlyList<TargetObjectInfo> GetAvailableTargets()
    {
        var scene = this.GetService<EditViewModel>()?.Scene;
        if (scene == null) return [];

        var searcher = new ObjectSearcher(scene, obj =>
            obj is Brush && obj is not IPresenter<Brush>);

        return searcher.SearchAll()
            .Cast<Brush>()
            .Select(b => new TargetObjectInfo(CoreObjectHelper.GetDisplayName(b), b, CoreObjectHelper.GetOwnerElement(b)))
            .ToList();
    }

    public IReadOnlyList<TargetObjectInfo> GetAvailableDrawableTargets()
    {
        var scene = this.GetService<EditViewModel>()?.Scene;
        if (scene == null) return [];

        var searcher = new ObjectSearcher(scene, obj =>
            obj is Drawable && obj is not IPresenter<Drawable>);

        return searcher.SearchAll()
            .Cast<Drawable>()
            .Select(b => new TargetObjectInfo(CoreObjectHelper.GetDisplayName(b), b, CoreObjectHelper.GetOwnerElement(b)))
            .ToList();
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
