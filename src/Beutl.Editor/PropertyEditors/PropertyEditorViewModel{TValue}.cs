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
using Beutl.ProjectSystem;
using Beutl.PropertyAdapters;
using Beutl.Reactive;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.PropertyEditors;

// A property editor's view model for a TValue property: it writes values, keyframes and expressions.
public abstract class PropertyEditorViewModel<TValue> : PropertyEditorViewModel
{
    private ReadOnlyReactiveProperty<TValue>? _shownValue;
    private bool _previewingExpression;
    private bool _relativePreview;
    private TValue? _historyStart;
    private TValue? _previewStart;
    private TValue? _shownStart;
    private TValue? _previewInput;

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

    // Expressions use the edited keyframe's time; separated components and properties without keys use the playhead.
    protected ReadOnlyReactiveProperty<TValue> ShownValue => _shownValue ??= HasExpression
        .CombineLatest(SeparatedComponents, (hasExpression, components) => hasExpression || components != null)
        .Select(evaluate =>
            evaluate && PropertyAdapter.GetEngineProperty() is IExpressionProperty<TValue> engineProperty
                ? ObserveShownValue(engineProperty)
                : EditingKeyFrame
                    .Select(x => (x as KeyFrame<TValue>)?.Value.GetObservable())
                    .Select(x => x ?? PropertyAdapter.GetObservable())
                    .Switch())
        .Switch()
        .ToReadOnlyReactiveProperty()
        .AddTo(Disposables)!;

    private IObservable<TValue> ObserveShownValue(IExpressionProperty<TValue> property)
        => ObserveEditingTime()
            .CombineLatest(EditingKeyFrame.Select(key => key?.KeyTime.GetObservable()
                ?? Observable.ReturnThenNever(MediaTime.Zero)).Switch(), (time, _) => time)
            .CombineLatest(ObservePropertyEdits(property), (time, _) => EvaluateShownValue(property, time));

    // A shared combined expression can read Value from its sibling properties, so edits to a sibling refresh this too.
    private static IObservable<int> ObservePropertyEdits(IExpressionProperty<TValue> property)
    {
        IObservable<int> edits = Observable.FromEventPattern(h => property.Edited += h, h => property.Edited -= h)
            .Select(_ => 0);

        if (property.GetOwnerObject() is { } owner)
        {
            foreach (IProperty first in owner.Properties)
            {
                if (first.GetAttributes()?.OfType<CombinedEditorAttribute>().FirstOrDefault() is not { } attribute)
                    continue;

                string[] others = attribute.OtherNames;
                if (!ReferenceEquals(first, property) && !others.Contains(property.Name))
                    continue;

                foreach (IProperty sibling in owner.Properties)
                {
                    if (ReferenceEquals(sibling, property))
                        continue;

                    if (ReferenceEquals(sibling, first) || others.Contains(sibling.Name))
                    {
                        edits = edits.Merge(Observable.FromEventPattern(h => sibling.Edited += h, h => sibling.Edited -= h)
                            .Select(_ => 0));
                    }
                }
                break;
            }
        }

        return edits.StartWith(0);
    }

    private TValue EvaluateShownValue(IExpressionProperty<TValue> property, MediaTime time)
    {
        if (SeparatedComponents.Value is IComponentAnimation<TValue> components)
        {
            if (!property.HasExpression) return components.GetEditingValue(time);
        }
        else if (property.HasExpression && EditingKeyFrame.Value is { } key && GetAnimation() is { } animation)
        {
            time = key.KeyTime.Value + AnimationOperations.GetTimeOrigin(animation, AnimationOwner);
        }

        return property.Evaluate(new EvaluationContext(time));
    }

    // Moving the owner changes expression times and edited component keys even if the playhead stays still.
    private IObservable<MediaTime> ObserveEditingTime()
        => (PropertyAdapter.GetEngineProperty()?.GetOwnerObject() as IHierarchical)
            ?.FindHierarchicalParent<EngineObject>(includeSelf: true) is { } owner
            ? CurrentTime.CombineLatest(owner.TimeRange.GetObservable(), (time, _) => time)
            : CurrentTime;

    public sealed override void Reset()
    {
        if (!IsElementEditable) return;
        // Every element goes back to the default of its own editor, which can differ from the one shown, as the rest
        // positions of pins do, and they all take it as one edit.
        if (MirrorsValue((editor, own) =>
            {
                if (editor.GetDefaultValue() is { } value)
                    editor.SetValue(editor.GetEditingValue(), own ? (TValue?)value : CopyForOtherElement((TValue?)value));
            }))
        {
            return;
        }

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
        // element's, so the others can still differ from it even when it stays. Other elements keep their expressions.
        TValue? mirroredNewValue = _previewingExpression ? GetEditingValue() : newValue;
        if (MirrorsValue(
                (editor, own) => editor.SetValue(
                    own ? oldValue : editor.GetEditingValue(),
                    own ? newValue : CopyForOtherElement(mirroredNewValue),
                    commandName),
                commitName: commandName))
        {
            return;
        }

        if (HasExpression.Value && !CanEditUnderExpression) return;
        if (_previewingExpression)
        {
            // Controls confirm the displayed result; the preview already wrote the underlying value.
            oldValue = _historyStart;
            newValue = GetEditingValue();
            _previewingExpression = false;
        }
        else if (HasExpression.Value)
        {
            oldValue = GetEditingValue();
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
        if (MirrorsValue((editor, own) => editor.SetValue(own ? newValue : CopyForOtherElement(newValue))))
        {
            return;
        }

        // Like SetValue(oldValue, newValue): replacing the current value can repair the element, as when a
        // file-backed editor puts a valid file in place of a source that loading dropped.
        _previewingExpression = false;
        if (IsDisposed || (HasExpression.Value && !CanEditUnderExpression) || !CanReplace(GetEditingValue(), newValue)) return;
        WriteValue(newValue);

        CompleteElementRepair();
        Commit();
    }

    // Runs edit on this editor and on each other element's editor that takes a value (own: whether it is this one).
    private bool MirrorsValue(Action<PropertyEditorViewModel<TValue>, bool> edit, bool commit = true, string? commitName = null)
        => Mirrors(
            (editor, _) =>
            {
                var typed = (PropertyEditorViewModel<TValue>)editor;
                bool own = ReferenceEquals(typed, this);
                if (own || !typed.HasExpression.Value || typed.CanEditUnderExpression)
                    edit(typed, own);
            },
            commit,
            commitName);

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

    protected virtual TValue OffsetValue(TValue value, TValue from, TValue to)
        => ComponentAnimations.OffsetValue(value, from, to);

    // What typed input writes below an expression: the whole value, as a picked color or a reset does. An editor with
    // a field per component overrides it to keep the components the user did not change.
    protected virtual TValue ReplaceUnderExpression(TValue underlying, TValue shown, TValue input) => input;

    protected virtual TValue ReplaceUnderExpression(TValue underlying, TValue shown, TValue input, int editedComponents)
        => ReplaceUnderExpression(underlying, shown, input);

    protected TValue? GetIncrementValue(TValue? shown)
        => _previewingExpression && _relativePreview ? _previewInput : shown;

    // A host with its own history session also ends previews on cancellation or capture loss.
    protected void EndValuePreview() => _previewingExpression = false;

    public TValue? SetCurrentValueAndGetCoerced(TValue? value)
        => SetCurrentValueAndGetCoerced(value, relative: true);

    internal TValue? SetCurrentValueAndGetCoerced(TValue? value, bool relative, bool directUnderlying = false, int? editedComponents = null)
    {
        if (IsDisposed) return default;
        if (!IsElementEditable || (HasExpression.Value && !CanEditUnderExpression))
            return ShownValue.Value;

        TValue? underlying = HasExpression.Value ? ComputeUnderlyingPreview(value, relative, directUnderlying, editedComponents) : value;
        if (Mirror != null)
        {
            TValue? coerced = default;
            if (MirrorsValue(
                    (editor, own) =>
                    {
                        if (own)
                        {
                            coerced = ApplyUnderlyingPreview(underlying);
                        }
                        else
                        {
                            editor.SetCurrentValue(CopyForOtherElement(underlying));
                        }
                    },
                    commit: false))
            {
                return coerced;
            }
        }

        return ApplyUnderlyingPreview(underlying);
    }

    private TValue? ComputeUnderlyingPreview(TValue? value, bool relative, bool directUnderlying, int? editedComponents)
    {
        if (!_previewingExpression)
        {
            _historyStart = GetEditingValue();
            StartPreviewBaseline(relative);
            _previewingExpression = true;
        }
        else if (relative != _relativePreview)
        {
            // Typing after a wheel step (or the reverse) keeps each input's meaning; undo still goes back to the start.
            StartPreviewBaseline(relative);
        }

        if (_relativePreview)
            return OffsetValue(_previewStart!, _shownStart!, value!);
        if (directUnderlying)
            return value;

        // Earlier keystrokes can change the displayed and underlying values used to apply this input.
        TValue underlying = GetEditingValue()!;
        return editedComponents is { } mask
            ? ReplaceUnderExpression(underlying, ShownValue.Value, value!, mask)
            : ReplaceUnderExpression(underlying, ShownValue.Value, value!);
    }

    private TValue? ApplyUnderlyingPreview(TValue? underlying)
    {
        if (HasExpression.Value)
        {
            TValue? accepted = SetCurrentValue(underlying);
            // Incremental controls continue from accepted input, not from the expression's result.
            _previewInput = _relativePreview ? OffsetValue(_shownStart!, _previewStart!, accepted!) : accepted;
            return ShownValue.Value;
        }

        _previewingExpression = false;
        return SetCurrentValue(underlying);
    }

    private void StartPreviewBaseline(bool relative)
    {
        _previewStart = GetEditingValue();
        _shownStart = ShownValue.Value;
        _relativePreview = relative;
    }

    // Also used by a combined editor, which owns the preview and commit for all its properties.
    internal TValue? SetCurrentValue(TValue? value)
    {
        if (!IsElementEditable) return GetEditingValue();
        WriteValue(value);
        InvalidateFrameCache();
        return GetEditingValue();
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
            newAnimation.KeyFrames.Add(new KeyFrame<TValue>
            {
                Value = { Value = animatableProperty.GetValue()! },
                Easing = { Value = new SplineEasing() },
                KeyTime = { Value = MediaTime.Zero }
            });

            animatableProperty.Animation = newAnimation;
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
        if (MirrorsSetExpression(expressionString, out bool set, out error))
            return set;
        if (!CanSetExpression(out error))
        {
            return false;
        }
        if (PropertyAdapter is IExpressionPropertyAdapter<TValue> expressionProperty)
        {
            if (!TryParseExpression(expressionProperty.Expression, expressionString, out IExpression<TValue>? newExpression, out error))
            {
                return false;
            }

            WriteExpression(newExpression);
        }

        error = null;
        return true;
    }

    internal override PropertyLinkExpressionWriter? CreateLinkExpressionWriter()
        => PropertyAdapter is IExpressionPropertyAdapter<TValue> adapter
            ? new PropertyLinkExpressionWriter<TValue>(
                adapter,
                GetService(typeof(Element)) as Element,
                GetService(typeof(Scene)) as Scene,
                GetService(typeof(HistoryManager)) as HistoryManager)
            : null;

    private static bool TryParseExpression(
        IExpression<TValue>? current,
        string expressionString,
        [NotNullWhen(true)] out IExpression<TValue>? expression,
        [NotNullWhen(false)] out string? error)
    {
        for (IExpression? expr = current; expr != null; expr = expr.Replaced)
        {
            if (expr is ICombinedValueExpression<TValue> combined)
            {
                return combined.TryParseSameContext(expressionString, out expression, out error);
            }
        }

        if (Expression.TryParse<TValue>(expressionString, out StringExpression<TValue>? plain, out error))
        {
            expression = plain;
            return true;
        }

        expression = null;
        return false;
    }

    // Sets an expression that another editor has parsed, keeping any animation below it.
    internal bool SetExpression(IExpression<TValue> expression, [NotNullWhen(false)] out string? error)
    {
        if (!CanSetExpression(out error))
        {
            return false;
        }

        WriteExpression(expression);
        return true;
    }

    // Setting an expression keeps the property's value and animation below it, so it only repairs a dropped value
    // when neither the kept value nor the kept animation still holds a recovery blocker.
    private bool CanSetExpression([NotNullWhen(false)] out string? error)
    {
        error = CanReplace(null, new object?[] { PropertyAdapter.GetValue(), GetAnimation() })
            ? null
            : MessageStrings.OperationFailed;
        return error == null;
    }

    private void WriteExpression(IExpression<TValue> expression)
    {
        if (PropertyAdapter is not IExpressionPropertyAdapter<TValue> expressionProperty) return;

        expressionProperty.Expression = expression;

        // An expression can replace a value that loading dropped.
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
