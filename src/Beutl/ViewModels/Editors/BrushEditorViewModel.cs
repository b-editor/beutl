using System.Text.Json.Nodes;
using Beutl.Animation;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Services;
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
                ? t.Item1?.Target.GetValue(RenderContext.Default)
                : null)
            .Select(fe => fe != null ? CoreObjectHelper.GetDisplayName(fe) : Message.Property_is_unset)
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

    public ReadOnlyReactivePropertySlim<bool> IsSolid { get; }

    public ReadOnlyReactivePropertySlim<bool> IsLinearGradient { get; }

    public ReadOnlyReactivePropertySlim<bool> IsConicGradient { get; }

    public ReadOnlyReactivePropertySlim<bool> IsRadialGradient { get; }

    public ReadOnlyReactivePropertySlim<bool> IsPerlinNoise { get; }

    public ReadOnlyReactivePropertySlim<bool> IsDrawable { get; }

    public ReadOnlyReactivePropertySlim<bool> IsPresenter { get; }

    public ReadOnlyReactivePropertySlim<string?> CurrentTargetName { get; }

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
            PropertyAdapter.SetValue(newValue);
            Commit();
        }
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
