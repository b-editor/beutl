using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

using Avalonia;
using Avalonia.Data;

using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Controls.PropertyEditors;
using Beutl.Framework;
using Beutl.Operation;
using Beutl.ProjectSystem;
using Beutl.ViewModels.Tools;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.ViewModels.Editors;

public abstract class BaseEditorViewModel : IPropertyEditorContext, IProvideEditViewModel
{
    protected CompositeDisposable Disposables = new();
    private bool _disposedValue;
    private EditViewModel? _editViewModel;
    private IDisposable? _currentFrameRevoker;
    private bool _skipKeyFrameIndexSubscription;

    protected BaseEditorViewModel(IAbstractProperty property)
    {
        WrappedProperty = property;

        CorePropertyMetadata metadata = property.Property.GetMetadata<CorePropertyMetadata>(property.ImplementedType);
        Header = metadata.DisplayAttribute?.GetName() ?? property.Property.Name;

        IObservable<bool> hasAnimation = property is IAbstractAnimatableProperty anm
            ? anm.ObserveAnimation.Select(x => x != null)
            : Observable.Return(false);

        IObservable<bool>? isReadOnly = Observable.Return(property.Property is IStaticProperty { CanWrite: false });

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
                .SelectMany(x => (x as IKeyFrameAnimation)?.KeyFrames.ObserveProperty(y => y.Count) ?? Observable.Return(0))
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
                    .SelectMany(x => x ?? Observable.Return<KeyFrames?>(default)))
                .CombineWithPrevious()
                .Subscribe(t =>
                {
                    (int oldIndex, _) = t.OldValue;
                    (int newIndex, KeyFrames? keyframes) = t.NewValue;

                    if (_editViewModel != null && keyframes != null
                        && 0 <= newIndex && newIndex < keyframes.Count)
                    {
                        EditingKeyFrame.Value = keyframes[newIndex];

                        if (!_skipKeyFrameIndexSubscription && newIndex != oldIndex)
                        {
                            _editViewModel.Scene.CurrentFrame = EditingKeyFrame.Value.KeyTime;
                        }
                    }
                    else
                    {
                        EditingKeyFrame.Value = null;
                    }

                    _skipKeyFrameIndexSubscription = false;
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

    public IAbstractProperty WrappedProperty { get; }

    public bool CanReset => GetDefaultValue() != null;

    public string Header { get; }

    public ReadOnlyReactivePropertySlim<bool> CanEdit { get; }

    public ReadOnlyReactivePropertySlim<bool> IsReadOnly { get; }

    public ReadOnlyReactivePropertySlim<bool> HasAnimation { get; }

    public ReactivePropertySlim<bool> IsSymbolIconFilled { get; } = new();

    public ReactivePropertySlim<IKeyFrame?> EditingKeyFrame { get; } = new();

    public ReadOnlyReactivePropertySlim<int> KeyFrameCount { get; }

    public ReactivePropertySlim<int> KeyFrameIndex { get; } = new();

    public bool IsAnimatable => WrappedProperty.Property.GetMetadata<CorePropertyMetadata>(WrappedProperty.ImplementedType).PropertyFlags.HasFlag(PropertyFlags.Animatable);

    public bool IsStylingSetter => WrappedProperty is IStylingSetterPropertyImpl;

    [AllowNull]
    public PropertyEditorExtension Extension { get; set; }

    EditViewModel? IProvideEditViewModel.EditViewModel => _editViewModel;

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

    public void WriteToJson(ref JsonNode json)
    {
    }

    public void ReadFromJson(JsonNode json)
    {
    }

    public virtual void Accept(IPropertyEditorContextVisitor visitor)
    {
        visitor.Visit(this);
        if (visitor is IProvideEditViewModel parentViewModel)
        {
            _currentFrameRevoker?.Dispose();
            _currentFrameRevoker = null;

            _editViewModel = parentViewModel.EditViewModel;
            if (WrappedProperty is IAbstractAnimatableProperty animatableProperty
                && _editViewModel != null)
            {
                _currentFrameRevoker = _editViewModel.Scene.GetObservable(Scene.CurrentFrameProperty)
                    .CombineLatest(animatableProperty.ObserveAnimation
                        .Select(x => (x as IKeyFrameAnimation)?.KeyFrames)
                        .Select(x => x?.CollectionChangedAsObservable()
                            .Select(_ => x)
                            .Publish(x)
                            .RefCount())
                        .SelectMany(x => x ?? Observable.Return<KeyFrames?>(default)))
                    .Subscribe(t =>
                    {
                        IsSymbolIconFilled.Value = t.Second?.Any(obj => obj.KeyTime == t.First) ?? false;
                        if (t.Second != null)
                        {
                            int kfIndex = t.Second.IndexAt(t.First);
                            _skipKeyFrameIndexSubscription = KeyFrameIndex.Value != kfIndex;
                            KeyFrameIndex.Value = kfIndex;
                        }
                    });
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
        ICorePropertyMetadata metadata = WrappedProperty.Property.GetMetadata<ICorePropertyMetadata>(WrappedProperty.ImplementedType);
        return metadata.GetDefaultValue();
    }

    protected virtual void Dispose(bool disposing)
    {
        Disposables.Dispose();
        _currentFrameRevoker?.Dispose();
        _currentFrameRevoker = null;
    }

    public virtual void InsertKeyFrame(TimeSpan keyTime)
    {
    }

    public virtual void RemoveKeyFrame(TimeSpan keyTime)
    {
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

    public override void InsertKeyFrame(TimeSpan keyTime)
    {
        if (WrappedProperty is IAbstractAnimatableProperty
            {
                Animation: KeyFrameAnimation<T> kfAnimation,
            }
            && !kfAnimation.KeyFrames.Any(x => x.KeyTime == keyTime))
        {
            var keyframe = new KeyFrame<T>
            {
                Value = kfAnimation.Interpolate(keyTime),
                Easing = new LinearEasing(),
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

    public override void RemoveKeyFrame(TimeSpan keyTime)
    {
        if (WrappedProperty is IAbstractAnimatableProperty
            {
                Animation: IKeyFrameAnimation kfAnimation,
                Property.PropertyType: { } ptype
            })
        {
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

    private sealed class SetCommand : IRecordableCommand
    {
        private readonly IAbstractProperty<T> _setter;
        private readonly T? _oldValue;
        private readonly T? _newValue;

        public SetCommand(IAbstractProperty<T> setter, T? oldValue, T? newValue)
        {
            _setter = setter;
            _oldValue = oldValue;
            _newValue = newValue;
        }

        public void Do()
        {
            _setter.SetValue(_newValue);
        }

        public void Redo()
        {
            Do();
        }

        public void Undo()
        {
            _setter.SetValue(_oldValue);
        }
    }

    private sealed class SetKeyFrameValueCommand : IRecordableCommand
    {
        private readonly KeyFrame<T> _keyframe;
        private readonly T? _oldValue;
        private readonly T? _newValue;

        public SetKeyFrameValueCommand(KeyFrame<T> setter, T? oldValue, T? newValue)
        {
            _keyframe = setter;
            _oldValue = oldValue;
            _newValue = newValue;
        }

        public void Do()
        {
            _keyframe.SetValue(KeyFrame<T>.ValueProperty, _newValue);
        }

        public void Redo()
        {
            Do();
        }

        public void Undo()
        {
            _keyframe.SetValue(KeyFrame<T>.ValueProperty, _oldValue);
        }
    }

    private sealed class AddKeyFrameCommand : IRecordableCommand
    {
        private readonly KeyFrames _keyFrames;
        private readonly IKeyFrame _keyFrame;

        public AddKeyFrameCommand(KeyFrames keyFrames, IKeyFrame keyFrame)
        {
            _keyFrames = keyFrames;
            _keyFrame = keyFrame;
        }

        public void Do()
        {
            _keyFrames.Add(_keyFrame, out _);
        }

        public void Redo()
        {
            Do();
        }

        public void Undo()
        {
            _keyFrames.Remove(_keyFrame);
        }
    }
}
