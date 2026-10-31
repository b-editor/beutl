using System.Diagnostics.CodeAnalysis;
using System.Reactive.Linq;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Composition;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.Extensibility;
using Beutl.Language;
using Beutl.PropertyAdapters;
using Beutl.Reactive;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.PropertyEditors;

// A property editor's view model for a TValue property: it writes values, keyframes and expressions.
public abstract class PropertyEditorViewModel<TValue> : PropertyEditorViewModel
{
    private ReadOnlyReactiveProperty<TValue>? _shownValue;

    protected PropertyEditorViewModel(IPropertyAdapter<TValue> property)
        : base(property)
    {
        EditingKeyFrame = base.EditingKeyFrame
            .Select(x => x as KeyFrame<TValue>)
            .ToReadOnlyReactiveProperty()
            .DisposeWith(Disposables);

        if (typeof(TValue).IsAssignableTo(typeof(CoreObject)))
        {
            PropertyAdapter.GetObservable()
                .Subscribe(value =>
                {
                    if (value is CoreObject obj)
                    {
                        var desc = PropertyAdapter.Description;
                        var typeDesc = TypeDisplayHelpers.GetLocalizedDescription(obj.GetType());
                        if (!string.IsNullOrEmpty(desc) && !string.IsNullOrEmpty(typeDesc))
                        {
                            Description.Value = $"{desc}\n{typeDesc}";
                        }
                        else if (!string.IsNullOrEmpty(typeDesc))
                        {
                            Description.Value = typeDesc;
                        }
                        else
                        {
                            Description.Value = desc;
                        }
                    }
                    else
                    {
                        Description.Value = PropertyAdapter.Description;
                    }
                })
                .DisposeWith(Disposables);
        }
    }

    public new IPropertyAdapter<TValue> PropertyAdapter => (IPropertyAdapter<TValue>)base.PropertyAdapter;

    public new ReadOnlyReactiveProperty<KeyFrame<TValue>?> EditingKeyFrame { get; }

    // The value to show: the expression's result at the current time, else the edited keyframe's or the property's.
    protected ReadOnlyReactiveProperty<TValue> ShownValue => _shownValue ??= HasExpression
        .CombineLatest(SeparatedComponents, (hasExpression, components) => hasExpression || components != null)
        .Select(evaluate =>
            evaluate && PropertyAdapter.GetEngineProperty() is IExpressionProperty<TValue> engineProperty
                ? ObserveEditingTime().CombineLatest(Observable.FromEventPattern(h => engineProperty.Edited += h, h => engineProperty.Edited -= h)
                    .Select(_ => 0).StartWith(0), (time, _) => !engineProperty.HasExpression && SeparatedComponents.Value is IComponentAnimation<TValue> split
                        ? split.GetEditingValue(time) : engineProperty.Evaluate(new EvaluationContext(time)))
                : EditingKeyFrame
                    .Select(x => (x as KeyFrame<TValue>)?.Value.GetObservable())
                    .Select(x => x ?? PropertyAdapter.GetObservable())
                    .Switch())
        .Switch()
        .ToReadOnlyReactiveProperty()
        .AddTo(Disposables)!;

    // Moving the owner changes expression times and edited component keys even if the playhead stays still.
    private IObservable<MediaTime> ObserveEditingTime()
        => (PropertyAdapter.GetEngineProperty()?.GetOwnerObject() as IHierarchical)
            ?.FindHierarchicalParent<EngineObject>(includeSelf: true) is { } owner
            ? CurrentTime.CombineLatest(owner.TimeRange.GetObservable(), (time, _) => time)
            : CurrentTime;

    public sealed override void Reset()
    {
        if (!IsElementEditable) return;
        if (GetDefaultValue() is { } defaultValue)
        {
            SetValue(GetEditingValue(), (TValue?)defaultValue);
        }
    }

    public void SetValue(TValue? oldValue, TValue? newValue)
    {
        SetValue(oldValue, newValue, null);
    }

    internal void SetValue(TValue? oldValue, TValue? newValue, string? commandName)
    {
        if (!CanReplace(oldValue, newValue)) return;
        if (!EqualityComparer<TValue>.Default.Equals(oldValue, newValue))
        {
            WriteValue(newValue);

            CompleteElementRepair();
            Commit(commandName);
        }
    }

    public void SetValue(TValue? newValue)
    {
        // Like SetValue(oldValue, newValue): replacing the current value can repair the element, as when a
        // file-backed editor puts a valid file in place of a source that loading dropped.
        if (IsDisposed || !CanReplace(GetEditingValue(), newValue)) return;
        WriteValue(newValue);

        CompleteElementRepair();
        Commit();
    }

    // The value of the keyframe being edited, or of the property itself when no keyframe is.
    protected internal TValue? GetEditingValue()
        => SeparatedComponents.Value is IComponentAnimation<TValue> components ? components.GetEditingValue(CurrentTimeValue)
            : EditingKeyFrame.Value is { } keyFrame ? keyFrame.Value.Value : PropertyAdapter.GetValue();

    // Writes to the keyframe being edited, or to the property itself when no keyframe is.
    private void WriteValue(TValue? newValue)
    {
        if (SeparatedComponents.Value is IComponentAnimation<TValue> components)
        {
            components.SetValue(newValue!, CurrentTimeValue);
        }
        else if (EditingKeyFrame.Value is { } kf)
        {
            kf.Value.Value = newValue!;
        }
        else
        {
            IPropertyAdapter<TValue> prop = PropertyAdapter;
            prop.SetValue(newValue);
        }
    }

    public TValue? SetCurrentValueAndGetCoerced(TValue? value)
    {
        if (IsDisposed) return default;
        if (!IsElementEditable)
            return GetEditingValue();
        if (SeparatedComponents.Value is IComponentAnimation<TValue> components)
        {
            components.SetValue(value!, CurrentTimeValue);
            InvalidateFrameCache();
            return components.GetEditingValue(CurrentTimeValue);
        }
        if (EditingKeyFrame.Value != null)
        {
            EditingKeyFrame.Value.Value.Value = value!;
            InvalidateFrameCache();
            return EditingKeyFrame.Value.Value.Value;
        }
        else
        {
            PropertyAdapter.SetValue(value);
            InvalidateFrameCache();
            return PropertyAdapter.GetValue();
        }
    }

    public override void InsertKeyFrame(MediaTime keyTime)
    {
        if (!IsElementEditable) return;
        if (GetAnimation() is not KeyFrameAnimation<TValue> kfAnimation) return;

        var keyframe = AnimationOperations.InsertKeyFrame(
            animation: kfAnimation,
            easing: null,
            keyTime: keyTime,
            logger: Logger);
        Commit();

        if (keyframe == null) return;

        int index = kfAnimation.KeyFrames.IndexOf(keyframe);
        if (index >= 0)
        {
            KeyFrameIndex.Value = index;
        }
    }

    public override void RemoveKeyFrame(MediaTime keyTime)
    {
        if (IsDisposed || GetAnimation() is not KeyFrameAnimation<TValue> kfAnimation) return;
        // Check the keyframe itself: removing another one while it holds an easing that could not be
        // read leaves the element protected, so that removal would not be saved.
        if (AnimationOperations.FindKeyFrame(kfAnimation, keyTime, Logger) is not { } keyframe
            || !CanReplace(keyframe)) return;

        AnimationOperations.RemoveKeyFrame(
            animation: kfAnimation,
            keyframe: keyframe,
            logger: Logger);
        CompleteElementRepair();
        Commit();
    }

    public override void PrepareToEditAnimation()
    {
        if (SeparatedComponents.Value != null) return;
        if (!IsElementEditable) return;
        if (PropertyAdapter is IAnimatablePropertyAdapter<TValue> animatableProperty
            && animatableProperty.Animation is not KeyFrameAnimation<TValue>)
        {
            var newAnimation = new KeyFrameAnimation<TValue>();
            TValue initialValue = animatableProperty.GetValue()!;
            newAnimation.KeyFrames.Add(new KeyFrame<TValue>
            {
                Value = { Value = initialValue },
                Easing = { Value = new SplineEasing() },
                KeyTime = { Value = MediaTime.Zero }
            });

            animatableProperty.Animation = newAnimation;
            if (PropertyAdapter is IExpressionPropertyAdapter<TValue> ep)
            {
                ep.Expression = null;
            }

            Commit();
        }
    }

    public override void RemoveAnimation()
    {
        if (!CanReplace(GetAnimation())) return;
        if (PropertyAdapter is IAnimatablePropertyAdapter<TValue> animatableProperty)
        {
            animatableProperty.Animation = null;
            CompleteElementRepair();
            Commit();
        }
    }

    public override bool SetExpression(string expressionString, [NotNullWhen(false)] out string? error)
    {
        // Setting an expression drops the animation, so it can discard an easing that could not be read.
        if (!CanReplace(GetAnimation()))
        {
            error = MessageStrings.OperationFailed;
            return false;
        }
        if (PropertyAdapter is IExpressionPropertyAdapter<TValue> expressionProperty)
        {
            if (!Expression.TryParse<TValue>(expressionString, out var newExpression, out error))
            {
                return false;
            }

            expressionProperty.Expression = newExpression;
            if (PropertyAdapter is IAnimatablePropertyAdapter<TValue> ap)
            {
                ap.Animation = null;
            }

            // Also for a property that cannot be animated: an expression replaces a value that loading dropped.
            CompleteElementRepair();
            Commit();
        }

        error = null;
        return true;
    }

    public override void RemoveExpression()
    {
        if (!IsElementEditable) return;
        if (PropertyAdapter is IExpressionPropertyAdapter<TValue> expressionProperty)
        {
            expressionProperty.Expression = null;
            Commit();
        }
    }

    public override string? GetExpressionString()
    {
        if (PropertyAdapter is IExpressionPropertyAdapter<TValue> expressionProperty)
        {
            return expressionProperty.Expression?.ExpressionString;
        }

        return null;
    }
}
