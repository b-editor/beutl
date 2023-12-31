using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

using Avalonia;
using Avalonia.Data;

using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Controls.PropertyEditors;
using Beutl.Operation;
using Beutl.ProjectSystem;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.ViewModels.Editors;

public abstract class BaseEditorViewModel : IPropertyEditorContext, IServiceProvider
{
    protected CompositeDisposable Disposables = [];
    private bool _disposedValue;
    private IDisposable? _currentFrameRevoker;
    private bool _skipKeyFrameIndexSubscription;
    private Element? _element;
    private EditViewModel? _editViewModel;
    private IServiceProvider? _parentServices;

    protected BaseEditorViewModel(IAbstractProperty property)
    {
        WrappedProperty = property;

        Header = property.DisplayName;

        IObservable<bool> hasAnimation = property is IAbstractAnimatableProperty anm
            ? anm.ObserveAnimation.Select(x => x != null)
            : Observable.Return(false);

        IObservable<bool>? isReadOnly = Observable.Return(property.IsReadOnly);

        CanEdit = isReadOnly
            .Not()
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);

        IsReadOnly = isReadOnly
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);

        HasAnimation = hasAnimation
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);

        if (property is IAbstractAnimatableProperty animatableProperty)
        {
            KeyFrameCount = animatableProperty.ObserveAnimation
                .Select(x => (x as IKeyFrameAnimation)?.KeyFrames.ObserveProperty(y => y.Count) ?? Observable.Return(0))
                .Switch()
                .ToReadOnlyReactivePropertySlim()
                .DisposeWith(Disposables);

            KeyFrameIndex
                .CombineLatest(animatableProperty.ObserveAnimation
                    .Select(x => (x as IKeyFrameAnimation)?.KeyFrames)
                    .Select(x => x?.CollectionChangedAsObservable()
                        .Do(_ => _skipKeyFrameIndexSubscription = true)
                        .Select(_ => x)
                        .Publish(x)
                        .RefCount())
                    .Select(x => x ?? Observable.Return<KeyFrames?>(default))
                    .Switch())
                .CombineWithPrevious()
                .Subscribe(t =>
                {
                    if (GetAnimation() is { } animation)
                    {
                        (float oldIndex, _) = t.OldValue;
                        (float newIndex, KeyFrames? keyframes) = t.NewValue;

                        if (_editViewModel != null && keyframes != null)
                        {
                            int newCeiled = (int)Math.Clamp(MathF.Ceiling(newIndex), 0, keyframes.Count - 1);
                            EditingKeyFrame.Value = keyframes[newCeiled];

                            if (!_skipKeyFrameIndexSubscription && newIndex != oldIndex)
                            {
                                TimeSpan start = _element?.Start ?? default;
                                TimeSpan keyTime = EditingKeyFrame.Value.KeyTime;
                                TimeSpan globalKeyTime = animation.UseGlobalClock ? keyTime : keyTime + start;

                                _editViewModel.Scene.CurrentFrame = globalKeyTime;
                            }
                        }
                        else
                        {
                            EditingKeyFrame.Value = null;
                        }

                        _skipKeyFrameIndexSubscription = false;
                    }
                    else
                    {
                        EditingKeyFrame.Value = null;
                    }
                })
                .DisposeWith(Disposables);
        }
        else
        {
            KeyFrameCount = new ReadOnlyReactivePropertySlim<int>(Observable.Return(0));
        }
    }

    ~BaseEditorViewModel()
    {
        if (!_disposedValue)
            Dispose(false);
    }

    public IAbstractProperty WrappedProperty { get; private set; }

    public bool CanReset => GetDefaultValue() != null;

    public string Header { get; }

    public ReadOnlyReactivePropertySlim<bool> CanEdit { get; }

    public ReadOnlyReactivePropertySlim<bool> IsReadOnly { get; }

    public ReadOnlyReactivePropertySlim<bool> HasAnimation { get; }

    public ReactivePropertySlim<bool> IsSymbolIconFilled { get; } = new();

    public ReactivePropertySlim<IKeyFrame?> EditingKeyFrame { get; } = new();

    public ReadOnlyReactivePropertySlim<int> KeyFrameCount { get; }

    public ReactivePropertySlim<float> KeyFrameIndex { get; } = new();

    public bool IsAnimatable => WrappedProperty is IAbstractAnimatableProperty;

    public bool IsStylingSetter => WrappedProperty is IStylingSetterPropertyImpl;

    [AllowNull]
    public PropertyEditorExtension Extension { get; set; }

    public void Dispose()
    {
        if (!_disposedValue)
        {
            Dispose(true);
            _disposedValue = true;
            GC.SuppressFinalize(this);
        }
    }

    public abstract void Reset();

    public virtual void WriteToJson(JsonObject json)
    {
    }

    public virtual void ReadFromJson(JsonObject json)
    {
    }

    public IAnimation? GetAnimation()
    {
        return (WrappedProperty as IAbstractAnimatableProperty)?.Animation;
    }

    public virtual void Accept(IPropertyEditorContextVisitor visitor)
    {
        visitor.Visit(this);
        if (visitor is IServiceProvider serviceProvider)
        {
            _parentServices = serviceProvider;
            _element = serviceProvider.GetService<Element>();
            _editViewModel = serviceProvider.GetService<EditViewModel>();

            if (_editViewModel != null)
            {
                _currentFrameRevoker?.Dispose();
                _currentFrameRevoker = null;

                if (WrappedProperty is IAbstractAnimatableProperty animatableProperty)
                {
                    _currentFrameRevoker = _editViewModel.Scene.GetObservable(Scene.CurrentFrameProperty)
                        .CombineLatest(animatableProperty.ObserveAnimation
                            .Select(x => (x as IKeyFrameAnimation)?.KeyFrames)
                            .Select(x => x?.CollectionChangedAsObservable()
                                .Select(_ => x)
                                .Publish(x)
                                .RefCount())
                            .Select(x => x ?? Observable.Return<KeyFrames?>(default))
                            .Switch())
                        .Subscribe(t =>
                        {
                            if (GetAnimation() is { } animation)
                            {
                                int rate = _editViewModel?.Scene?.FindHierarchicalParent<Project>().GetFrameRate() ?? 30;

                                TimeSpan globalkeyTime = t.First;
                                TimeSpan localKeyTime = _element != null ? globalkeyTime - _element.Start : globalkeyTime;
                                TimeSpan keyTime = animation.UseGlobalClock ? globalkeyTime : localKeyTime;
                                keyTime = keyTime.RoundToRate(rate);

                                IsSymbolIconFilled.Value = t.Second?.Any(obj => obj.KeyTime == keyTime) ?? false;
                                if (t.Second != null)
                                {
                                    float kfIndex = t.Second.IndexAtOrCount(keyTime);
                                    if (!IsSymbolIconFilled.Value)
                                        kfIndex -= 0.5f;

                                    _skipKeyFrameIndexSubscription = KeyFrameIndex.Value != kfIndex;
                                    KeyFrameIndex.Value = kfIndex;
                                }
                            }
                        });
                }
            }
        }
        else if (visitor is PropertyEditor editor)
        {
            editor[!PropertyEditor.IsReadOnlyProperty] = IsReadOnly.ToBinding();
            editor.Header = Header;
            if (WrappedProperty is IAbstractAnimatableProperty animatableProperty)
            {
                editor[!PropertyEditor.KeyFrameCountProperty] = KeyFrameCount.ToBinding();
                editor[!PropertyEditor.KeyFrameIndexProperty] = KeyFrameIndex.ToPropertyBinding(BindingMode.TwoWay);
            }
        }
    }

    protected object? GetDefaultValue()
    {
        return WrappedProperty.GetDefaultValue();
    }

    protected virtual void Dispose(bool disposing)
    {
        Disposables.Dispose();
        _currentFrameRevoker?.Dispose();
        _currentFrameRevoker = null;
        _editViewModel = null!;
        _parentServices = null;
        _element = null;
        WrappedProperty = null!;
    }

    public virtual void InsertKeyFrame(TimeSpan keyTime)
    {
    }

    public virtual void RemoveKeyFrame(TimeSpan keyTime)
    {
    }

    public virtual void PrepareToEditAnimation()
    {
    }

    public virtual void RemoveAnimation()
    {
    }

    public virtual object? GetService(Type serviceType)
    {
        if (serviceType.IsAssignableTo(typeof(IAbstractProperty)))
            return WrappedProperty;

        return _parentServices?.GetService(serviceType);
    }
}

public abstract class BaseEditorViewModel<T> : BaseEditorViewModel
{
    protected BaseEditorViewModel(IAbstractProperty<T> property)
        : base(property)
    {
        EditingKeyFrame = base.EditingKeyFrame
            .Select(x => x as KeyFrame<T>)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);
    }

    public new IAbstractProperty<T> WrappedProperty => (IAbstractProperty<T>)base.WrappedProperty;

    public new ReadOnlyReactivePropertySlim<KeyFrame<T>?> EditingKeyFrame { get; }

    public sealed override void Reset()
    {
        if (GetDefaultValue() is { } defaultValue)
        {
            SetValue(WrappedProperty.GetValue(), (T?)defaultValue);
        }
    }

    public void SetValue(T? oldValue, T? newValue)
    {
        if (!EqualityComparer<T>.Default.Equals(oldValue, newValue))
        {
            if (EditingKeyFrame.Value != null)
            {
                CommandRecorder.Default.DoAndPush(new SetKeyFrameValueCommand(EditingKeyFrame.Value, oldValue, newValue));
            }
            else
            {
                CommandRecorder.Default.DoAndPush(new SetCommand(WrappedProperty, oldValue, newValue));
            }
        }
    }

    public T? SetCurrentValueAndGetCoerced(T? value)
    {
        if (EditingKeyFrame.Value != null)
        {
            EditingKeyFrame.Value.Value = value!;
            return EditingKeyFrame.Value.Value;
        }
        else
        {
            WrappedProperty.SetValue(value);
            return WrappedProperty.GetValue();
        }
    }

    private TimeSpan ConvertKeyTime(TimeSpan globalkeyTime, IAnimation animation)
    {
        Element? element = this.GetService<Element>();
        TimeSpan localKeyTime = element != null ? globalkeyTime - element.Start : globalkeyTime;
        TimeSpan keyTime = animation.UseGlobalClock ? globalkeyTime : localKeyTime;

        int rate = this.GetService<EditViewModel>()?.Scene?.FindHierarchicalParent<Project>() is { } proj ? proj.GetFrameRate() : 30;

        return keyTime.RoundToRate(rate);
    }

    public override void InsertKeyFrame(TimeSpan keyTime)
    {
        if (GetAnimation() is KeyFrameAnimation<T> kfAnimation)
        {
            keyTime = ConvertKeyTime(keyTime, kfAnimation);
            if (!kfAnimation.KeyFrames.Any(x => x.KeyTime == keyTime))
            {
                var keyframe = new KeyFrame<T>
                {
                    Value = kfAnimation.Interpolate(keyTime),
                    Easing = new SplineEasing(),
                    KeyTime = keyTime
                };

                var command = new AddKeyFrameCommand(kfAnimation.KeyFrames, keyframe);
                command.DoAndRecord(CommandRecorder.Default);

                int index = kfAnimation.KeyFrames.IndexOf(keyframe);
                if (index >= 0)
                {
                    KeyFrameIndex.Value = index;
                }
            }
        }
    }

    public override void RemoveKeyFrame(TimeSpan keyTime)
    {
        if (WrappedProperty is IAbstractAnimatableProperty
            {
                Animation: IKeyFrameAnimation kfAnimation,
                PropertyType: { } ptype
            })
        {
            keyTime = ConvertKeyTime(keyTime, kfAnimation);
            IKeyFrame? keyframe = kfAnimation.KeyFrames.FirstOrDefault(x => x.KeyTime == keyTime);
            if (keyframe != null)
            {
                kfAnimation.KeyFrames.BeginRecord<IKeyFrame>()
                    .Remove(keyframe)
                    .ToCommand()
                    .DoAndRecord(CommandRecorder.Default);
            }
        }
    }

    public override void PrepareToEditAnimation()
    {
        if (WrappedProperty is IAbstractAnimatableProperty<T> animatableProperty
            && animatableProperty.Animation is not KeyFrameAnimation<T>
            && animatableProperty.GetCoreProperty() is CoreProperty<T> coreProperty)
        {
            var command = new PrepareAnimationCommand(animatableProperty, coreProperty);
            command.DoAndRecord(CommandRecorder.Default);
        }
    }

    public override void RemoveAnimation()
    {
        if (WrappedProperty is IAbstractAnimatableProperty<T> animatableProperty)
        {
            var command = new RemoveAnimationCommand(animatableProperty);
            command.DoAndRecord(CommandRecorder.Default);
        }
    }

    private sealed class SetCommand(IAbstractProperty<T> setter, T? oldValue, T? newValue) : IRecordableCommand
    {
        public void Do()
        {
            setter.SetValue(newValue);
        }

        public void Redo()
        {
            Do();
        }

        public void Undo()
        {
            setter.SetValue(oldValue);
        }
    }

    private sealed class SetKeyFrameValueCommand(KeyFrame<T> setter, T? oldValue, T? newValue) : IRecordableCommand
    {
        public void Do()
        {
            setter.SetValue(KeyFrame<T>.ValueProperty, newValue);
        }

        public void Redo()
        {
            Do();
        }

        public void Undo()
        {
            setter.SetValue(KeyFrame<T>.ValueProperty, oldValue);
        }
    }

    private sealed class AddKeyFrameCommand(KeyFrames keyFrames, IKeyFrame keyFrame) : IRecordableCommand
    {
        public void Do()
        {
            keyFrames.Add(keyFrame, out _);
        }

        public void Redo()
        {
            Do();
        }

        public void Undo()
        {
            keyFrames.Remove(keyFrame);
        }
    }

    private sealed class PrepareAnimationCommand : IRecordableCommand
    {
        private readonly IAbstractAnimatableProperty<T> _property;
        private readonly IAnimation<T>? _oldAnimation;
        private readonly KeyFrameAnimation<T>? _newAnimation;

        public PrepareAnimationCommand(IAbstractAnimatableProperty<T> property, CoreProperty<T> coreProperty)
        {
            _property = property;
            _oldAnimation = _property.Animation;
            _newAnimation = new KeyFrameAnimation<T>(coreProperty);
            T initialValue = property.GetValue()!;
            _newAnimation.KeyFrames.Add(new KeyFrame<T>
            {
                Value = initialValue,
                Easing = new SplineEasing(),
                KeyTime = TimeSpan.Zero
            });
        }

        public void Do()
        {
            _property.Animation = _newAnimation;
        }

        public void Redo()
        {
            Do();
        }

        public void Undo()
        {
            _property.Animation = _oldAnimation;
        }
    }

    private sealed class RemoveAnimationCommand : IRecordableCommand
    {
        private readonly IAbstractAnimatableProperty<T> _property;
        private readonly IAnimation<T>? _oldAnimation;

        public RemoveAnimationCommand(IAbstractAnimatableProperty<T> property)
        {
            _property = property;
            _oldAnimation = _property.Animation;
        }

        public void Do()
        {
            _property.Animation = null;
        }

        public void Redo()
        {
            Do();
        }

        public void Undo()
        {
            _property.Animation = _oldAnimation;
        }
    }
}
