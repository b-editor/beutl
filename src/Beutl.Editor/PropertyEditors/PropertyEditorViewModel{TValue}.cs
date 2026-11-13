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
        // Every element takes the value as its own editor would, from what it shows itself: the value shown is one
        // element's, so the others can still differ from it even when it stays. One whose value an expression gives
        // takes none, as its own editor lets no value be entered.
        if (Mirrors(
                (editor, _) =>
                {
                    var typed = (PropertyEditorViewModel<TValue>)editor;
                    if (ReferenceEquals(typed, this))
                        typed.SetValue(oldValue, newValue, commandName);
                    else if (!typed.HasExpression.Value)
                        typed.SetValue(typed.GetEditingValue(), CopyForOtherElement(newValue), commandName);
                },
                commitName: commandName))
        {
            return;
        }

        if (!CanReplace(oldValue, newValue)) return;
        if (!EqualityComparer<TValue>.Default.Equals(oldValue, newValue))
        {
            WriteValue(newValue);

            CompleteElementRepair();
            Commit(commandName);
        }
    }

    public override bool CanMatchOtherElements => true;

    public override void MatchOtherElements()
    {
        if (IsDisposed || !CanEdit.Value) return;

        TValue? value = GetEditingValue();
        SetValue(value, value, null);
    }

    public void SetValue(TValue? newValue)
    {
        if (Mirrors((editor, _) =>
            {
                var typed = (PropertyEditorViewModel<TValue>)editor;
                if (ReferenceEquals(typed, this))
                    typed.SetValue(newValue);
                else if (!typed.HasExpression.Value)
                    typed.SetValue(CopyForOtherElement(newValue));
            }))
        {
            return;
        }

        // Like SetValue(oldValue, newValue): replacing the current value can repair the element, as when a
        // file-backed editor puts a valid file in place of a source that loading dropped.
        if (IsDisposed || !CanReplace(GetEditingValue(), newValue)) return;
        WriteValue(newValue);

        CompleteElementRepair();
        Commit();
    }

    // An object goes to each element as a copy of its own; a value without identity goes as it is.
    private TValue? CopyForOtherElement(TValue? value)
        => value is null || typeof(TValue).IsValueType ? value : (TValue?)Mirror!.CopyValue(value);

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

        // While a value is dragged every element shows it, and the edit that sets it takes one history entry for all.
        TValue? coerced = default;
        if (Mirrors(
                (editor, _) =>
                {
                    var typed = (PropertyEditorViewModel<TValue>)editor;
                    if (ReferenceEquals(typed, this))
                        coerced = typed.SetCurrentValueAndGetCoerced(value);
                    else if (!typed.HasExpression.Value)
                        typed.SetCurrentValueAndGetCoerced(CopyForOtherElement(value));
                },
                commit: false))
        {
            return coerced;
        }

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
        if (Mirrors((editor, shift) => editor.InsertKeyFrame(keyTime + shift))) return;
        if (!IsElementEditable) return;
        if (GetAnimation() is not KeyFrameAnimation<TValue> kfAnimation) return;

        var keyframe = AnimationOperations.InsertKeyFrame(
            animation: kfAnimation,
            easing: null,
            keyTime: keyTime,
            logger: Logger,
            fallbackOwner: AnimationOwner,
            fallbackScene: EditingScene);
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
        if (Mirrors((editor, shift) => editor.RemoveKeyFrame(keyTime + shift))) return;
        if (IsDisposed || GetAnimation() is not KeyFrameAnimation<TValue> kfAnimation) return;
        // Check the keyframe itself: removing another one while it holds an easing that could not be
        // read leaves the element protected, so that removal would not be saved.
        if (AnimationOperations.FindKeyFrame(kfAnimation, keyTime, Logger, AnimationOwner, EditingScene) is not { } keyframe
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
        if (Mirrors((editor, _) => editor.PrepareToEditAnimation())) return;
        if (SeparatedComponents.Value != null) return;
        if (!IsElementEditable) return;
        if (PropertyAdapter is IAnimatablePropertyAdapter<TValue> animatableProperty
            && animatableProperty.Animation is not KeyFrameAnimation<TValue>)
        {
            var newAnimation = new KeyFrameAnimation<TValue>();
            // The keyframes take over from an expression, so they start at the value it shows.
            TValue initialValue = PropertyAdapter.GetEngineProperty() is IExpressionProperty<TValue> { HasExpression: true } engineProperty
                ? engineProperty.Evaluate(new EvaluationContext(CurrentTimeValue))
                : animatableProperty.GetValue()!;
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
        if (Mirrors((editor, _) => editor.RemoveAnimation())) return;
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
        if (MirrorsExpression(editor => (editor.SetExpression(expressionString, out string? failure), failure), out bool set, out error))
            return set;
        if (!CanSetExpression(out error))
        {
            return false;
        }
        if (PropertyAdapter is IExpressionPropertyAdapter<TValue>)
        {
            if (!Expression.TryParse<TValue>(expressionString, out var newExpression, out error))
            {
                return false;
            }

            WriteExpression(newExpression);
        }

        error = null;
        return true;
    }

    // Puts an expression that another editor has read in place of this property's animation.
    internal bool SetExpression(IExpression<TValue> expression, [NotNullWhen(false)] out string? error)
    {
        if (!CanSetExpression(out error))
        {
            return false;
        }

        WriteExpression(expression);
        return true;
    }

    // Setting an expression drops the animation, so it can discard an easing that could not be read.
    private bool CanSetExpression([NotNullWhen(false)] out string? error)
    {
        error = CanReplace(GetAnimation()) ? null : MessageStrings.OperationFailed;
        return error == null;
    }

    private void WriteExpression(IExpression<TValue> expression)
    {
        if (PropertyAdapter is not IExpressionPropertyAdapter<TValue> expressionProperty) return;

        expressionProperty.Expression = expression;
        if (PropertyAdapter is IAnimatablePropertyAdapter<TValue> ap)
        {
            ap.Animation = null;
        }

        // Also for a property that cannot be animated: an expression replaces a value that loading dropped.
        CompleteElementRepair();
        Commit();
    }

    public override void RemoveExpression()
    {
        if (Mirrors((editor, _) => editor.RemoveExpression())) return;
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
