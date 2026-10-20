using System.Text.Json.Nodes;
using Avalonia.Input;
using Beutl.Animation;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

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
            .CombineLatest(ObserveExtensionProvider())
            .Select(t => t.First != null ? new PropertiesEditorViewModel(t.First, t.Second) : null)
            .DisposePreviousValue()
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

        (IsFallback, ActualTypeName, FallbackMessage) = FallbackEditorHelper.ObserveFallbackInfo(Value, Disposables);

        (IsPresenter, CurrentTargetName) = PresenterEditorHelper.ObservePresenter<Brush>(Value, Disposables);
    }

    private void AcceptChildren(PropertiesEditorViewModel? obj)
    {
        NestedEditorContextHelper.AcceptChildren(new ChildVisitor(this), null, obj);
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

    public IObservable<string?> GetJsonString() => FallbackHelper.GetFallbackJson(Value);

    public void SetJsonString(string? str)
    {
        Brush? previous = GetEditingValue();
        SetValue(previous, FallbackHelper.DeserializeInstance<Brush>(str));
    }

    public void UpdateBrushPreview()
    {
        _update?.Invoke();
    }

    public override void Reset()
    {
        if (GetDefaultValue() is { } defaultValue)
        {
            SetValue(GetEditingValue(), (Brush?)defaultValue);
        }
    }

    public void SetValue(Brush? oldValue, Brush? newValue)
    {
        SetValue(oldValue, newValue, null);
    }

    private void SetValue(Brush? oldValue, Brush? newValue, string? commandName)
    {
        if (!CanReplace(oldValue, newValue)) return;
        if (!EqualityComparer<Brush>.Default.Equals(oldValue, newValue))
        {
            if (EditingKeyFrame.Value is KeyFrame<Brush?> keyFrame)
            {
                keyFrame.Value = newValue;
            }
            else
            {
                PropertyAdapter.SetValue(newValue);
            }

            CompleteElementRepair();
            Commit(commandName);
        }
    }

    private Brush? GetEditingValue()
    {
        return EditingKeyFrame.Value is KeyFrame<Brush?> keyFrame
            ? keyFrame.Value
            : (Brush?)PropertyAdapter.GetValue();
    }

    protected override ICoreSerializable? GetCopyTarget()
        => Value.Value is Brush brush and not FallbackBrush ? brush : null;

    protected override ICoreSerializable? GetTemplateTarget() => GetCopyTarget();

    public override bool ApplyTemplate(ObjectTemplateItem template)
    {
        // SetValue checks the value it replaces.
        if (!CanReplaceContent) return false;
        if (template.CreateInstance() is not Brush instance) return false;
        IsExpanded.Value = true;
        SetValue(GetEditingValue(), instance, CommandNames.ApplyTemplate);
        return true;
    }

    public override bool TryPasteJson(string json)
    {
        // SetValue checks the value it replaces.
        if (!CanReplaceContent) return false;
        if (!CoreObjectClipboard.TryDeserializeJson<Brush>(json, out var pasted)) return false;

        IsExpanded.Value = true;
        SetValue(GetEditingValue(), pasted, CommandNames.PasteObject);
        return true;
    }

    public void SetColor(Color oldValue, Color newValue)
    {
        if (!IsElementEditable) return;
        if (Value.Value is SolidColorBrush solid)
        {
            solid.Color.CurrentValue = newValue;
            Commit();
        }
    }

    public void InsertGradientStop(int index, GradientStop item)
        => GradientStopEdits.Insert(this, (Value.Value as GradientBrush)?.GradientStops, index, item);

    public void RemoveGradientStop(int index)
        => GradientStopEdits.Remove(this, (Value.Value as GradientBrush)?.GradientStops, index);

    public void ConfirmeGradientStop(int oldIndex, int newIndex)
        => GradientStopEdits.Confirm(this, (Value.Value as GradientBrush)?.GradientStops, oldIndex, newIndex);

    // These check the part they replace, not the whole brush: replacing the drawable while the brush's
    // transform could not be loaded leaves the element protected, so that change would not be saved.
    public void ChangeDrawableType(Type type)
    {
        if (IsDisposed || Value.Value is not Media.DrawableBrush drawable) return;
        if (!CanReplace(drawable.Drawable.CurrentValue)) return;
        if (Activator.CreateInstance(type) is Drawable instance)
        {
            drawable.Drawable.CurrentValue = instance;
            CompleteElementRepair();
            Commit();
        }
    }

    public void SetTarget(Brush? target)
    {
        if (IsDisposed || Value.Value is not IPresenter<Brush> presenter) return;
        if (!CanReplace(presenter.Target.CurrentValue)) return;
        PresenterEditorHelper.AssignTarget(presenter, target);
        CompleteElementRepair();
        Commit();
    }

    public void SetDrawableTarget(Drawable? target)
    {
        if (IsDisposed) return;
        // A kept brush has only its drawable, or the target of its drawable presenter, replaced.
        object? replaced = Value.Value switch
        {
            DrawableBrush { Drawable.CurrentValue: DrawablePresenter current } => current.Target.CurrentValue,
            DrawableBrush kept => kept.Drawable.CurrentValue,
            var brush => brush,
        };
        if (!CanReplace(replaced)) return;

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

        PresenterEditorHelper.AssignTarget(presenter, target);

        CompleteElementRepair();
        Commit();
    }

    public IReadOnlyList<TargetObjectInfo> GetAvailableTargets()
        => TargetObjectSearchHelper.GetAvailableTargets<Brush>(this);

    public IReadOnlyList<TargetObjectInfo> GetAvailableDrawableTargets()
        => TargetObjectSearchHelper.GetAvailableTargets<Drawable>(this);

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
        NestedEditorContextHelper.ReadNestedJson(json, IsExpanded, ChildContext.Value);
    }

    public override void WriteToJson(JsonObject json)
    {
        base.WriteToJson(json);
        NestedEditorContextHelper.WriteNestedJson(json, IsExpanded.Value, ChildContext.Value);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        ChildContext.Value?.Dispose();
        _revoker?.Dispose();
    }
}
