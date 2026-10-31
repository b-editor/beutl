using System.Diagnostics.CodeAnalysis;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text.Json.Nodes;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Api.Services;
using Beutl.Editor.Services;
using Beutl.Extensibility;
using Beutl.Language;
using Beutl.Logging;
using Beutl.Media;
using Beutl.NodeGraph;
using Beutl.ProjectSystem;
using Beutl.PropertyAdapters;
using Beutl.Reactive;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.PropertyEditors;

// The view model of a property editor, without Avalonia: the host binds its control and adds the menu.
public abstract class PropertyEditorViewModel : IPropertyEditorContext, IServiceProvider
{
    protected readonly ILogger Logger;
    private readonly BehaviorSubject<MediaTime> _currentTime;
    private readonly string? _unitSymbol;
    private readonly SynchronizationContext? _synchronizationContext = SynchronizationContext.Current;
    protected CompositeDisposable Disposables = [];
    private IDisposable? _currentFrameRevoker;
    private bool _skipKeyFrameIndexSubscription;
    private Element? _element;
    private Scene? _scene;
    private GraphNode? _node;
    private IDisposable? _elementEditabilitySubscription;
    private readonly ReactivePropertySlim<bool> _isElementEditable = new(true);
    private IDisposable? _animationRepairSubscription;
    private readonly ReactivePropertySlim<bool> _canReplaceAnimation = new(true);
    private IEditorClock? _clock;
    private IServiceProvider? _parentServices;
    private IPropertyEditorControlHost? _propertyEditorControlHost;
    // The ExtensionProvider Accept injects; editors that build child contexts wait for it.
    private readonly ReactivePropertySlim<ExtensionProvider?> _extensionProvider = new();

    protected PropertyEditorViewModel(IPropertyAdapter property)
    {
        Logger = Log.CreateLogger(GetType());
        PropertyAdapter = property;

        Attribute[] attributes = property.GetAttributes();
        _unitSymbol = attributes.OfType<UnitAttribute>().FirstOrDefault()?.Unit switch
        {
            PropertyUnit.Percent => "%",
            PropertyUnit.Degrees => "°",
            PropertyUnit.Ratio => "×",
            _ => null
        };
        Header = FormatHeader(property.DisplayName);
        Description = new ReactivePropertySlim<string?>(property.Description).AddTo(Disposables);
        Type propertyType = property.PropertyType;
        HoverInfo = new ReactivePropertySlim<string?>().AddTo(Disposables);
        Description
            .Select(description => PropertyHoverInfoFormatter.Format(propertyType, description, attributes))
            .Subscribe(value => HoverInfo.Value = value)
            .DisposeWith(Disposables);

        // Keeps the clock's last time for a value subscribed after Accept; completes during disposal so stale clock
        // writes are ignored.
        _currentTime = new BehaviorSubject<MediaTime>(MediaTime.Zero);
        CurrentTime = _currentTime.AsObservable();

        SeparatedComponents = property.GetEngineProperty() is { } engineProperty
            ? Observable.FromEventPattern(h => engineProperty.Edited += h, h => engineProperty.Edited -= h)
                .Select(_ => engineProperty.GetPropertyAnimation() as IComponentAnimation)
                .StartWith(engineProperty.GetPropertyAnimation() as IComponentAnimation)
                .DistinctUntilChanged().ToReadOnlyReactivePropertySlim().AddTo(Disposables)
            : Observable.ReturnThenNever<IComponentAnimation?>(null).ToReadOnlyReactivePropertySlim().AddTo(Disposables);

        IObservable<bool> hasAnimation = property is IAnimatablePropertyAdapter anm
            ? anm.ObserveAnimation.Select(x => x != null)
            : Observable.ReturnThenNever(false);

        IObservable<bool> hasExpression = property is IExpressionPropertyAdapter expr
            ? expr.ObserveExpression.Select(x => x != null)
            : Observable.ReturnThenNever(false);

        CanEditProperty = _isElementEditable
            .Select(editable => editable && !property.IsReadOnly)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);

        CanEdit = CanEditProperty
            .CombineLatest(hasExpression, (canEdit, hasExpr) => canEdit && !hasExpr)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);

        HasAnimation = hasAnimation
            .ToReadOnlyReactiveProperty()
            .AddTo(Disposables);

        CanRemoveAnimation = _canReplaceAnimation
            .CombineLatest(hasAnimation, (canReplace, has) => canReplace && has && !property.IsReadOnly)
            .ToReadOnlyReactivePropertySlim()
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
                    .Select(x => x ?? Observable.ReturnThenNever<IListProperty<IKeyFrame>?>(default))
                    .Switch())
                .CombineWithPrevious()
                .Subscribe(SyncEditingKeyFrame)
                .DisposeWith(Disposables);
        }
        else
        {
            KeyFrameCount = new ReadOnlyReactiveProperty<int>(Observable.ReturnThenNever(0));
        }
    }

    ~PropertyEditorViewModel()
    {
        if (IsDisposed) return;

        Logger.LogWarning("Finalizer called on {TypeName}", GetType().Name);
        // Disposed on the thread that created it, as its subscriptions expect.
        _synchronizationContext?.Post(_ =>
        {
            Dispose(false);
            IsDisposed = true;
        }, null);
    }

    public bool IsDisposed { get; private set; }

    public IPropertyAdapter PropertyAdapter { get; private set; }

    public ReadOnlyReactivePropertySlim<IComponentAnimation?> SeparatedComponents { get; }

    public bool CanSeparateComponents => ComponentSeparation.CanSeparate(PropertyAdapter.GetEngineProperty());

    public void SeparateComponents()
    {
        if (!IsElementEditable || PropertyAdapter.IsReadOnly || !CanSeparateComponents || SeparatedComponents.Value != null) return;
        ComponentAnimations.Separate(PropertyAdapter.GetEngineProperty()!);
        Commit();
    }

    public void UnseparateComponents()
    {
        if (!IsElementEditable || PropertyAdapter.IsReadOnly || SeparatedComponents.Value == null) return;
        ComponentAnimations.Unseparate(PropertyAdapter.GetEngineProperty()!, CurrentTimeValue);
        Commit();
    }

    internal void Own(IDisposable disposable) => Disposables.Add(disposable);

    public bool CanReset => GetDefaultValue() != null;

    public string Header { get; }

    public string FormatHeader(string displayName) => _unitSymbol is null ? displayName : $"{displayName} ({_unitSymbol})";

    public ReactivePropertySlim<string?> Description { get; }

    public ReactivePropertySlim<string?> HoverInfo { get; }

    public ReadOnlyReactivePropertySlim<bool> CanEdit { get; }

    // Expressions prevent editing the value, but must still be editable/removable
    // on an unlocked clip. Menus use this state rather than CanEdit.
    public ReadOnlyReactivePropertySlim<bool> CanEditProperty { get; }

    protected internal bool IsElementEditable => !IsDisposed && ElementEditability.IsEditable(_element, _scene);

    // Replacing or removing content is how the user repairs a recovery-protected element, so those
    // edits check this instead of IsElementEditable: they stay allowed there while the content holds
    // what could not be loaded, or while loading dropped the property's value (see ElementEditability).
    protected internal bool CanReplace(object? content)
    {
        if (IsDisposed) return false;
        if (ElementEditability.CanRepair(_element, content, _scene)
            || ElementEditability.CanRepairDroppedValue(_element, PropertyAdapter.GetEngineProperty(), _scene)) return true;

        // An easing is a value, not an object of its own: the keyframe holds an easing that could not be read.
        return PropertyAdapter is EnginePropertyAdapter<Easing> { Object: KeyFrame keyFrame }
               && ElementEditability.CanRepair(_element, keyFrame, _scene);
    }

    // CanReplace for an edit that writes replacement in place of content. On a recovery-protected
    // element, a replacement that itself holds what could not be loaded never completes the repair,
    // so the edit would be lost on save.
    protected internal bool CanReplace(object? content, object? replacement)
        => CanReplace(content)
           && !(_element is { IsRecoveryProtected: true } && SceneRecovery.HoldsRecoveryBlocker(replacement));

    // CanReplace for everything the property holds: its value and its animation.
    protected internal bool CanReplaceContent
        => !IsDisposed && CanReplace(new object?[] { PropertyAdapter.GetValue(), GetAnimation() });

    public ReadOnlyReactiveProperty<bool> HasAnimation { get; }

    // Like CanEditProperty for "Remove animation", but also true on a recovery-protected element while
    // the animation holds an easing that could not be read, because removing it repairs the element.
    public ReadOnlyReactivePropertySlim<bool> CanRemoveAnimation { get; }

    public ReadOnlyReactiveProperty<bool> HasExpression { get; }

    // True while the playhead sits on a keyframe; the menu fills its keyframe icon.
    public ReactivePropertySlim<bool> IsAtKeyFrame { get; } = new();

    public ReactivePropertySlim<IKeyFrame?> EditingKeyFrame { get; } = new();

    public ReadOnlyReactiveProperty<int> KeyFrameCount { get; }

    public ReactivePropertySlim<float> KeyFrameIndex { get; } = new();

    public IObservable<MediaTime> CurrentTime { get; }

    protected MediaTime CurrentTimeValue => _currentTime.Value;

    [AllowNull] public PropertyEditorExtension Extension { get; set; }

    protected void CompleteElementRepair()
    {
        if (_element is not null)
        {
            ElementRecoveryService.TryCompleteRepair(_element, this.GetRequiredService<HistoryManager>());
        }
    }

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

    public IKeyFrameAnimation? GetAnimation()
    {
        return (PropertyAdapter as IAnimatablePropertyAdapter)?.Animation;
    }

    // Follows the keyframe index slider: edits the keyframe it points at and, when the user moved it,
    // seeks the clock to that keyframe.
    private void SyncEditingKeyFrame(((float First, IListProperty<IKeyFrame>? Second) OldValue, (float First, IListProperty<IKeyFrame>? Second) NewValue) t)
    {
        if (GetAnimation() is { } animation)
        {
            (float oldIndex, _) = t.OldValue;
            (float newIndex, IListProperty<IKeyFrame>? keyframes) = t.NewValue;

            if (_clock != null && keyframes is { Count: > 0 })
            {
                int newCeiled = (int)Math.Clamp(MathF.Ceiling(newIndex), 0, keyframes.Count - 1);
                EditingKeyFrame.Value = keyframes[newCeiled];

                if (!_skipKeyFrameIndexSubscription && newIndex != oldIndex)
                {
                    _clock.Seek(EditingKeyFrame.Value.KeyTime.Value + AnimationOperations.GetTimeOrigin(animation, _node));
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
    }

    // Marks the playhead sitting on a keyframe, and moves the index slider to it.
    private void UpdateKeyFrameIndicator((MediaTime First, IListProperty<IKeyFrame>? Second) t)
    {
        if (GetAnimation() is { } animation)
        {
            Rational rate = _scene?.FindHierarchicalParent<Project>().GetFrameRate() ?? new Rational(30);

            MediaTime keyTime = (t.First - AnimationOperations.GetTimeOrigin(animation, _node))
                .RoundToFrame(rate, MidpointRounding.AwayFromZero);

            IKeyFrame? atFrame = t.Second != null ? AnimationOperations.FindKeyFrameOnFrame(t.Second, keyTime, rate) : null;
            IsAtKeyFrame.Value = atFrame != null;
            if (t.Second != null)
            {
                float kfIndex = atFrame != null ? t.Second.IndexOf(atFrame) : t.Second.IndexAtOrCount(keyTime) - 0.5f;

                _skipKeyFrameIndexSubscription = KeyFrameIndex.Value != kfIndex;
                KeyFrameIndex.Value = kfIndex;
            }
        }
    }

    public virtual void Accept(IPropertyEditorContextVisitor visitor)
    {
        visitor.Visit(this);
        if (visitor is IServiceProvider serviceProvider)
        {
            _parentServices = serviceProvider;
            _propertyEditorControlHost = serviceProvider.GetService<IPropertyEditorControlHost>()?.CreateChildHost(PropertyAdapter);
            _element = serviceProvider.GetService<Element>();
            _scene = serviceProvider.GetService<Scene>();
            _node = serviceProvider.GetService<GraphNode>();
            _elementEditabilitySubscription?.Dispose();
            _elementEditabilitySubscription = ElementEditability.Observe(_element, _scene)
                .Subscribe(editable => _isElementEditable.Value = editable);
            _animationRepairSubscription?.Dispose();
            _animationRepairSubscription = PropertyAdapter is IAnimatablePropertyAdapter animatable
                ? animatable.ObserveAnimation
                    .Select(animation => ElementEditability.ObserveRepair(_element, animation, _scene))
                    .Switch()
                    .Subscribe(canReplace => _canReplaceAnimation.Value = canReplace)
                : null;
            _clock = serviceProvider.GetService<IEditorClock>();
            _extensionProvider.Value = serviceProvider.GetService<ExtensionProvider>();

            if (_clock != null)
            {
                _currentFrameRevoker?.Dispose();
                _currentFrameRevoker = null;

                if (PropertyAdapter is IAnimatablePropertyAdapter animatableProperty)
                {
                    _currentFrameRevoker = _clock.CurrentTime
                        .Do(_currentTime.OnNext)
                        .CombineLatest(animatableProperty.ObserveAnimation
                            .Select(animation => animation == null
                                ? Observable.ReturnThenNever<IListProperty<IKeyFrame>?>(null)
                                : animation.KeyFrames.CollectionChangedAsObservable()
                                    .Select(_ => animation.KeyFrames)
                                    .StartWith(animation.KeyFrames)
                                    .CombineLatest(AnimationOperations.ObserveTimeOrigin(animation, _node),
                                        (keyFrames, _) => keyFrames))
                            .Switch())
                        .Subscribe(UpdateKeyFrameIndicator);
                }
                else
                {
                    _currentFrameRevoker = _clock.CurrentTime
                        .Subscribe(_currentTime.OnNext);
                }
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
        // Stop new clock writes before completing the subject.
        _currentFrameRevoker?.Dispose();
        _currentFrameRevoker = null;
        _elementEditabilitySubscription?.Dispose();
        _elementEditabilitySubscription = null;
        _animationRepairSubscription?.Dispose();
        _animationRepairSubscription = null;
        _currentTime.OnCompleted();

        Disposables.Dispose();
        _extensionProvider.Dispose();
        _isElementEditable.Dispose();
        _canReplaceAnimation.Dispose();
        IsAtKeyFrame.Dispose();
        _parentServices = null;
        _propertyEditorControlHost = null;
        _element = null;
        _scene = null;
        PropertyAdapter = null!;
    }

    public virtual void InsertKeyFrame(MediaTime keyTime)
    {
    }

    public virtual void RemoveKeyFrame(MediaTime keyTime)
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

    // Connects a control that edits this view model's values; false for any other control.
    internal virtual bool TryConnect(object control) => false;

    public virtual object? GetService(Type serviceType)
    {
        if (serviceType == typeof(IPropertyEditorControlHost))
            return _propertyEditorControlHost;
        if (serviceType.IsAssignableTo(typeof(IPropertyAdapter)))
            return PropertyAdapter;

        return _parentServices?.GetService(serviceType);
    }

    // For editors that build child contexts after Accept. Resolves the ExtensionProvider itself so
    // hosts without an EditViewModel work too.
    protected ExtensionProvider GetExtensionProvider()
    {
        return this.GetRequiredService<ExtensionProvider>();
    }

    // Fires once Accept has injected the ExtensionProvider.
    protected IObservable<ExtensionProvider> ObserveExtensionProvider()
    {
        return _extensionProvider.Where(x => x != null)!;
    }

    public void InvalidateFrameCache()
    {
        if (_element is { } element)
        {
            this.GetService<IFrameCacheInvalidator>()?.Invalidate(element.Range);
        }
    }
}
