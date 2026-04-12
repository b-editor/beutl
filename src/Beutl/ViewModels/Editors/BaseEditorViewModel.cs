using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reactive.Subjects;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Threading;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor;
using Beutl.Editor.Components.Helpers;
using Beutl.Engine.Expressions;
using Beutl.Logging;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.ViewModels.Editors;

public abstract class BaseEditorViewModel : IPropertyEditorContext, IServiceProvider
{
    protected readonly ILogger Logger;
    private readonly Subject<TimeSpan> _currentTime;
    protected CompositeDisposable Disposables = [];
    private IDisposable? _currentFrameRevoker;
    private bool _skipKeyFrameIndexSubscription;
    private Element? _element;
    private EditViewModel? _editViewModel;
    private IServiceProvider? _parentServices;

    protected BaseEditorViewModel(IPropertyAdapter property)
    {
        Logger = Log.CreateLogger(GetType());
        PropertyAdapter = property;

        Header = property.DisplayName;
        Description = new ReactivePropertySlim<string?>(property.Description).AddTo(Disposables);

        _currentTime = new Subject<TimeSpan>().DisposeWith(Disposables);
        CurrentTime = _currentTime.Publish(TimeSpan.Zero).RefCount();

        IObservable<bool> hasAnimation = property is IAnimatablePropertyAdapter anm
            ? anm.ObserveAnimation.Select(x => x != null)
            : Observable.ReturnThenNever(false);

        IObservable<bool> hasExpression = property is IExpressionPropertyAdapter expr
            ? expr.ObserveExpression.Select(x => x != null)
            : Observable.ReturnThenNever(false);

        IObservable<bool>? isReadOnly = Observable.ReturnThenNever(property.IsReadOnly);

        // TODO: CanEditとIsReadOnlyどちらかだけにしたい
        CanEdit = isReadOnly
            .Not()
            .CombineLatest(hasExpression, (canEdit, hasExpr) => canEdit && !hasExpr)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);

        IsReadOnly = CanEdit.Not()
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);

        HasAnimation = hasAnimation
            .ToReadOnlyReactiveProperty()
            .AddTo(Disposables);

        HasExpression = hasExpression
            .ToReadOnlyReactiveProperty()
            .AddTo(Disposables);

        if (property is IAnimatablePropertyAdapter animatableProperty)
        {
            KeyFrameCount = animatableProperty.ObserveAnimation
                .Select(x => (x as IKeyFrameAnimation)?.KeyFrames.ObserveProperty(y => y.Count) ?? Observable.ReturnThenNever(0))
                .Switch()
                .ToReadOnlyReactiveProperty()
                .DisposeWith(Disposables);

            KeyFrameIndex
                .CombineLatest(animatableProperty.ObserveAnimation
                    .Select(x => (x as IKeyFrameAnimation)?.KeyFrames)
                    .Select(x => x?.CollectionChangedAsObservable()
                        .Do(_ => _skipKeyFrameIndexSubscription = true)
                        .Select(_ => x)
                        .Publish(x)
                        .RefCount())
                    .Select(x => x ?? Observable.ReturnThenNever<KeyFrames?>(default))
                    .Switch())
                .CombineWithPrevious()
                .Subscribe(t =>
                {
                    if (GetAnimation() is { } animation)
                    {
                        (float oldIndex, _) = t.OldValue;
                        (float newIndex, KeyFrames? keyframes) = t.NewValue;

                        if (_editViewModel != null && keyframes is { Count: > 0 })
                        {
                            int newCeiled = (int)Math.Clamp(MathF.Ceiling(newIndex), 0, keyframes.Count - 1);
                            EditingKeyFrame.Value = keyframes[newCeiled];

                            if (!_skipKeyFrameIndexSubscription && newIndex != oldIndex)
                            {
                                TimeSpan start = _element?.Start ?? default;
                                TimeSpan keyTime = EditingKeyFrame.Value.KeyTime;

                                _editViewModel.CurrentTime.Value = animation.UseGlobalClock ? keyTime : keyTime + start;
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
            KeyFrameCount = new ReadOnlyReactiveProperty<int>(Observable.ReturnThenNever(0));
        }
    }

    ~BaseEditorViewModel()
    {
        if (IsDisposed) return;

        Logger.LogWarning("Finalizer called on {TypeName}", GetType().Name);
        Dispatcher.UIThread.Post(() =>
        {
            Dispose(false);
            IsDisposed = true;
        });
    }

    public bool IsDisposed { get; private set; }

    public IPropertyAdapter PropertyAdapter { get; private set; }

    public bool CanReset => GetDefaultValue() != null;

    public string Header { get; }

    public ReactivePropertySlim<string?> Description { get; }

    public ReadOnlyReactivePropertySlim<bool> CanEdit { get; }

    public ReadOnlyReactivePropertySlim<bool> IsReadOnly { get; }

    public ReadOnlyReactiveProperty<bool> HasAnimation { get; }

    public ReadOnlyReactiveProperty<bool> HasExpression { get; }

    public ReactivePropertySlim<bool> IsSymbolIconFilled { get; } = new();

    public ReactivePropertySlim<IKeyFrame?> EditingKeyFrame { get; } = new();

    public ReadOnlyReactiveProperty<int> KeyFrameCount { get; }

    public ReactivePropertySlim<float> KeyFrameIndex { get; } = new();

    public IObservable<TimeSpan> CurrentTime { get; }

    public bool IsAnimatable => PropertyAdapter is IAnimatablePropertyAdapter;

    [AllowNull] public PropertyEditorExtension Extension { get; set; }

    protected ImmutableArray<CoreObject?> GetStorables() => [_element];

    public void Dispose()
    {
        if (!IsDisposed)
        {
            Dispose(true);
            IsDisposed = true;
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
        return (PropertyAdapter as IAnimatablePropertyAdapter)?.Animation;
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

                if (PropertyAdapter is IAnimatablePropertyAdapter animatableProperty)
                {
                    _currentFrameRevoker = _editViewModel.CurrentTime
                        .Do(_currentTime.OnNext)
                        .CombineLatest(animatableProperty.ObserveAnimation
                            .Select(x => (x as IKeyFrameAnimation)?.KeyFrames)
                            .Select(x => x?.CollectionChangedAsObservable()
                                .Select(_ => x)
                                .Publish(x)
                                .RefCount())
                            .Select(x => x ?? Observable.ReturnThenNever<KeyFrames?>(default))
                            .Switch())
                        .Subscribe(t =>
                        {
                            if (GetAnimation() is { } animation)
                            {
                                int rate = _editViewModel?.Scene?.FindHierarchicalParent<Project>().GetFrameRate() ??
                                           30;

                                TimeSpan globalkeyTime = t.First;
                                TimeSpan localKeyTime =
                                    _element != null ? globalkeyTime - _element.Start : globalkeyTime;
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
                else
                {
                    _currentFrameRevoker = _editViewModel.CurrentTime
                        .Subscribe(_currentTime.OnNext);
                }
            }
        }
        else if (visitor is PropertyEditor editor)
        {
            editor[!PropertyEditor.IsReadOnlyProperty] = IsReadOnly.ToBinding();
            editor.Header = Header;
            editor[!PropertyEditor.DescriptionProperty] = Description.ToBinding();
            if (PropertyAdapter is IAnimatablePropertyAdapter animatableProperty)
            {
                editor[!PropertyEditor.KeyFrameCountProperty] = KeyFrameCount.ToBinding();
                editor[!PropertyEditor.KeyFrameIndexProperty] = KeyFrameIndex.ToPropertyBinding(BindingMode.TwoWay);
            }
        }
    }

    public void Commit(string? name = null)
    {
        this.GetRequiredService<HistoryManager>().Commit(name ?? CommandNames.EditProperty);
    }

    protected object? GetDefaultValue()
    {
        return PropertyAdapter.GetDefaultValue();
    }

    protected virtual void Dispose(bool disposing)
    {
        Disposables.Dispose();
        _canPaste.Dispose();
        _currentFrameRevoker?.Dispose();
        _currentFrameRevoker = null;
        _editViewModel = null!;
        _parentServices = null;
        _element = null;
        PropertyAdapter = null!;
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

    public virtual bool SetExpression(string expressionString, [NotNullWhen(false)] out string? error)
    {
        error = null;
        return true;
    }

    public virtual void RemoveExpression()
    {
    }

    public virtual string? GetExpressionString()
    {
        return null;
    }

    // 任意のICoreSerializableオブジェクトのクリップボードコピー&ペースト対応
    private static readonly IReadOnlyReactiveProperty<bool> s_alwaysFalse
        = new ReactivePropertySlim<bool>(false);

    private readonly ReactivePropertySlim<bool> _canPaste = new(false);

    public virtual IReadOnlyReactiveProperty<bool> CanCopy => s_alwaysFalse;

    public IReadOnlyReactiveProperty<bool> CanPaste => _canPaste;

    protected virtual DataFormat<string>? PasteFormat => null;

    protected virtual ICoreSerializable? GetCopyTarget() => null;

    public virtual bool TryPasteJson(string json) => false;

    public virtual async ValueTask<bool> CopyAsync()
    {
        if (PasteFormat is not { } format) return false;
        if (GetCopyTarget() is not { } obj) return false;
        return await CoreObjectClipboard.CopyAsync(obj, format);
    }

    public virtual async ValueTask<bool> PasteAsync()
    {
        if (PasteFormat is not { } format) return false;
        var clipboard = ClipboardHelper.GetClipboard();
        if (clipboard == null) return false;
        string? json = await CoreObjectClipboard.TryGetJsonAsync(clipboard, format);
        return json != null && TryPasteJson(json);
    }

    // テンプレート保存・適用対応
    public virtual IReadOnlyReactiveProperty<bool> CanSaveAsTemplate => s_alwaysFalse;

    protected virtual Type? TemplateBaseType => null;

    protected virtual ICoreSerializable? GetTemplateTarget() => null;

    public virtual bool ApplyTemplate(ObjectTemplateItem template) => false;

    public IEnumerable<ObjectTemplateItem> GetApplicableTemplates()
        => TemplateBaseType is { } t ? ObjectTemplateService.Instance.FindByBaseType(t) : [];

    public ValueTask<bool> SaveAsTemplateAsync(string name)
    {
        if (GetTemplateTarget() is not { } target) return new(false);
        ObjectTemplateService.Instance.AddFromInstance(target, name);
        return new(true);
    }

    public async ValueTask RefreshCanPasteAsync()
    {
        if (PasteFormat is not { } format)
        {
            _canPaste.Value = false;
            return;
        }
        var clipboard = ClipboardHelper.GetClipboard();
        if (clipboard == null)
        {
            _canPaste.Value = false;
            return;
        }
        try
        {
            string? json = await CoreObjectClipboard.TryGetJsonAsync(clipboard, format);
            _canPaste.Value = json != null;
        }
        catch
        {
            _canPaste.Value = false;
        }
    }

    public virtual object? GetService(Type serviceType)
    {
        if (serviceType.IsAssignableTo(typeof(IPropertyAdapter)))
            return PropertyAdapter;

        return _parentServices?.GetService(serviceType) ?? _editViewModel?.GetService(serviceType);
    }

    public void InvalidateFrameCache()
    {
        if (this.GetService<EditViewModel>() is { Player: { } player, FrameCacheManager.Value: { } cacheManager })
        {
            Task.Run(() =>
            {
                int rate = player.GetFrameRate();
                ImmutableArray<CoreObject?> storables = GetStorables();
                IEnumerable<TimeRange> affectedRange = storables.OfType<Element>().Select(v => v.Range);

                cacheManager.DeleteAndUpdateBlocks(affectedRange
                    .Select(item => (Start: (int)item.Start.ToFrameNumber(rate),
                        End: (int)Math.Ceiling(item.End.ToFrameNumber(rate)))));
            });
        }
    }
}

public abstract class BaseEditorViewModel<T> : BaseEditorViewModel
{
    protected BaseEditorViewModel(IPropertyAdapter<T> property)
        : base(property)
    {
        EditingKeyFrame = base.EditingKeyFrame
            .Select(x => x as KeyFrame<T>)
            .ToReadOnlyReactiveProperty()
            .DisposeWith(Disposables);

        if (typeof(T).IsAssignableTo(typeof(CoreObject)))
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

    public new IPropertyAdapter<T> PropertyAdapter => (IPropertyAdapter<T>)base.PropertyAdapter;

    public new ReadOnlyReactiveProperty<KeyFrame<T>?> EditingKeyFrame { get; }

    public sealed override void Reset()
    {
        if (GetDefaultValue() is { } defaultValue)
        {
            SetValue(PropertyAdapter.GetValue(), (T?)defaultValue);
        }
    }

    public void SetValue(T? oldValue, T? newValue)
    {
        if (!EqualityComparer<T>.Default.Equals(oldValue, newValue))
        {
            if (EditingKeyFrame.Value is { } kf)
            {
                kf.Value = newValue!;
            }
            else
            {
                IPropertyAdapter<T> prop = PropertyAdapter;
                prop.SetValue(newValue);
            }

            Commit();
        }
    }

    public void SetValue(T? newValue)
    {
        if (EditingKeyFrame.Value is { } kf)
        {
            kf.Value = newValue!;
        }
        else
        {
            IPropertyAdapter<T> prop = PropertyAdapter;
            prop.SetValue(newValue);
        }

        Commit();
    }

    public T? SetCurrentValueAndGetCoerced(T? value)
    {
        if (EditingKeyFrame.Value != null)
        {
            EditingKeyFrame.Value.Value = value!;
            InvalidateFrameCache();
            return EditingKeyFrame.Value.Value;
        }
        else
        {
            PropertyAdapter.SetValue(value);
            InvalidateFrameCache();
            return PropertyAdapter.GetValue();
        }
    }

    public override void InsertKeyFrame(TimeSpan keyTime)
    {
        if (GetAnimation() is not KeyFrameAnimation<T> kfAnimation) return;

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

    public override void RemoveKeyFrame(TimeSpan keyTime)
    {
        if (GetAnimation() is not KeyFrameAnimation<T> kfAnimation) return;

        AnimationOperations.RemoveKeyFrame(
            animation: kfAnimation,
            keyTime: keyTime,
            logger: Logger);
        Commit();
    }

    public override void PrepareToEditAnimation()
    {
        if (PropertyAdapter is IAnimatablePropertyAdapter<T> animatableProperty
            && animatableProperty.Animation is not KeyFrameAnimation<T>)
        {
            var newAnimation = new KeyFrameAnimation<T>();
            T initialValue = animatableProperty.GetValue()!;
            newAnimation.KeyFrames.Add(new KeyFrame<T>
            {
                Value = initialValue,
                Easing = new SplineEasing(),
                KeyTime = TimeSpan.Zero
            });

            animatableProperty.Animation = newAnimation;
            if (PropertyAdapter is IExpressionPropertyAdapter<T> ep)
            {
                ep.Expression = null;
            }

            Commit();
        }
    }

    public override void RemoveAnimation()
    {
        if (PropertyAdapter is IAnimatablePropertyAdapter<T> animatableProperty)
        {
            animatableProperty.Animation = null;
            Commit();
        }
    }

    public override bool SetExpression(string expressionString, [NotNullWhen(false)] out string? error)
    {
        if (PropertyAdapter is IExpressionPropertyAdapter<T> expressionProperty)
        {
            if (!Expression.TryParse<T>(expressionString, out var newExpression, out error))
            {
                return false;
            }

            expressionProperty.Expression = newExpression;
            if (PropertyAdapter is IAnimatablePropertyAdapter<T> ap)
            {
                ap.Animation = null;
            }

            Commit();
        }

        error = null;
        return true;
    }

    public override void RemoveExpression()
    {
        if (PropertyAdapter is IExpressionPropertyAdapter<T> expressionProperty)
        {
            expressionProperty.Expression = null;
            Commit();
        }
    }

    public override string? GetExpressionString()
    {
        if (PropertyAdapter is IExpressionPropertyAdapter<T> expressionProperty)
        {
            return expressionProperty.Expression?.ExpressionString;
        }

        return null;
    }
}
