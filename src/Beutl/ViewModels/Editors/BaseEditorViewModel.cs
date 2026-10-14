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
using Beutl.Editor.Services;
using Beutl.Engine.Expressions;
using Beutl.Logging;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.PropertyAdapters;
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
    private Scene? _scene;
    private IDisposable? _elementEditabilitySubscription;
    private readonly ReactivePropertySlim<bool> _isElementEditable = new(true);
    private IDisposable? _animationRepairSubscription;
    private readonly ReactivePropertySlim<bool> _canReplaceAnimation = new(true);
    private EditViewModel? _editViewModel;
    private IEditorClock? _clock;
    private IServiceProvider? _parentServices;
    private IPropertyEditorControlHost? _propertyEditorControlHost;
    // The ExtensionProvider Accept injects; editors that build child contexts wait for it.
    private readonly ReactivePropertySlim<Beutl.Api.Services.ExtensionProvider?> _extensionProvider = new();

    protected BaseEditorViewModel(IPropertyAdapter property)
    {
        Logger = Log.CreateLogger(GetType());
        PropertyAdapter = property;

        Header = property.DisplayName;
        Description = new ReactivePropertySlim<string?>(property.Description).AddTo(Disposables);
        Type propertyType = property.PropertyType;
        Attribute[] attributes = property.GetAttributes();
        HoverInfo = new ReactivePropertySlim<string?>().AddTo(Disposables);
        Description
            .Select(description => PropertyHoverInfoFormatter.Format(propertyType, description, attributes))
            .Subscribe(value => HoverInfo.Value = value)
            .DisposeWith(Disposables);

        // Complete during disposal so stale clock writes are ignored.
        _currentTime = new Subject<TimeSpan>();
        CurrentTime = _currentTime.Publish(TimeSpan.Zero).RefCount();

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

        // TODO: CanEditとIsReadOnlyどちらかだけにしたい
        CanEdit = CanEditProperty
            .CombineLatest(hasExpression, (canEdit, hasExpr) => canEdit && !hasExpr)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);

        IsReadOnly = CanEdit.Inverse()
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
                    .Select(x => x ?? Observable.ReturnThenNever<KeyFrames?>(default))
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

    public ReactivePropertySlim<string?> HoverInfo { get; }

    public ReadOnlyReactivePropertySlim<bool> CanEdit { get; }

    // Expressions prevent editing the value, but must still be editable/removable
    // on an unlocked clip. Menus use this state rather than CanEdit.
    public ReadOnlyReactivePropertySlim<bool> CanEditProperty { get; }

    protected internal bool IsElementEditable => !IsDisposed && ElementEditability.IsEditable(_element, _scene);

    // Replacing or removing content is how the user repairs a recovery-protected element, so those
    // edits check this instead of IsElementEditable: they stay allowed there while the content holds
    // what could not be loaded (see ElementEditability.CanRepair).
    protected internal bool CanReplace(object? content)
    {
        if (IsDisposed) return false;
        if (ElementEditability.CanRepair(_element, content, _scene)) return true;

        // An easing is a value, not an object of its own: the keyframe holds an easing that could not be read.
        return PropertyAdapter is CorePropertyAdapter<Easing> { Object: KeyFrame keyFrame }
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

    public ReadOnlyReactivePropertySlim<bool> IsReadOnly { get; }

    public ReadOnlyReactiveProperty<bool> HasAnimation { get; }

    // Like CanEditProperty for "Remove animation", but also true on a recovery-protected element while
    // the animation holds an easing that could not be read, because removing it repairs the element.
    public ReadOnlyReactivePropertySlim<bool> CanRemoveAnimation { get; }

    public ReadOnlyReactiveProperty<bool> HasExpression { get; }

    public ReactivePropertySlim<FluentIcons.Common.IconVariant> SymbolIconVariant { get; } = new(FluentIcons.Common.IconVariant.Regular);

    public ReactivePropertySlim<IKeyFrame?> EditingKeyFrame { get; } = new();

    public ReadOnlyReactiveProperty<int> KeyFrameCount { get; }

    public ReactivePropertySlim<float> KeyFrameIndex { get; } = new();

    public IObservable<TimeSpan> CurrentTime { get; }

    [AllowNull] public PropertyEditorExtension Extension { get; set; }

    protected void CompleteElementRepair()
    {
        if (_element is not null)
        {
            Beutl.Editor.Services.ElementRecoveryService.TryCompleteRepair(
                _element, this.GetRequiredService<HistoryManager>());
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

    public IAnimation? GetAnimation()
    {
        return (PropertyAdapter as IAnimatablePropertyAdapter)?.Animation;
    }

    // Follows the keyframe index slider: edits the keyframe it points at and, when the user moved it,
    // seeks the clock to that keyframe.
    private void SyncEditingKeyFrame(((float First, KeyFrames? Second) OldValue, (float First, KeyFrames? Second) NewValue) t)
    {
        if (GetAnimation() is { } animation)
        {
            (float oldIndex, _) = t.OldValue;
            (float newIndex, KeyFrames? keyframes) = t.NewValue;

            if (_clock != null && keyframes is { Count: > 0 })
            {
                int newCeiled = (int)Math.Clamp(MathF.Ceiling(newIndex), 0, keyframes.Count - 1);
                EditingKeyFrame.Value = keyframes[newCeiled];

                if (!_skipKeyFrameIndexSubscription && newIndex != oldIndex)
                {
                    TimeSpan start = _element?.Start ?? default;
                    TimeSpan keyTime = EditingKeyFrame.Value.KeyTime;

                    _clock.CurrentTime.Value = animation.UseGlobalClock ? keyTime : keyTime + start;
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

    // Fills the keyframe icon when the playhead sits on a keyframe, and moves the index slider to it.
    private void UpdateKeyFrameIndicator((TimeSpan First, KeyFrames? Second) t)
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

            SymbolIconVariant.Value = t.Second?.Any(obj => obj.KeyTime == keyTime) ?? false
                ? FluentIcons.Common.IconVariant.Filled
                : FluentIcons.Common.IconVariant.Regular;
            if (t.Second != null)
            {
                float kfIndex = t.Second.IndexAtOrCount(keyTime);
                if (SymbolIconVariant.Value != FluentIcons.Common.IconVariant.Filled)
                    kfIndex -= 0.5f;

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
            _editViewModel = serviceProvider.GetService<EditViewModel>();
            _scene = serviceProvider.GetService<Scene>() ?? _editViewModel?.Scene;
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
            _extensionProvider.Value = serviceProvider.GetService<Beutl.Api.Services.ExtensionProvider>()
                ?? _editViewModel?.ExtensionProvider;

            if (_clock != null)
            {
                _currentFrameRevoker?.Dispose();
                _currentFrameRevoker = null;

                if (PropertyAdapter is IAnimatablePropertyAdapter animatableProperty)
                {
                    _currentFrameRevoker = _clock.CurrentTime
                        .Do(_currentTime.OnNext)
                        .CombineLatest(animatableProperty.ObserveAnimation
                            .Select(x => (x as IKeyFrameAnimation)?.KeyFrames)
                            .Select(x => x?.CollectionChangedAsObservable()
                                .Select(_ => x)
                                .Publish(x)
                                .RefCount())
                            .Select(x => x ?? Observable.ReturnThenNever<KeyFrames?>(default))
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
        else if (visitor is PropertyEditor editor)
        {
            editor[!PropertyEditor.IsReadOnlyProperty] = IsReadOnly.ToBinding();
            editor.Header = Header;
            editor[!PropertyEditor.DescriptionProperty] = Description.ToBinding();
            editor[!PropertyEditor.HoverInfoProperty] = HoverInfo.ToBinding();
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
        // Stop new clock writes before completing the subject.
        _currentFrameRevoker?.Dispose();
        _currentFrameRevoker = null;
        _elementEditabilitySubscription?.Dispose();
        _elementEditabilitySubscription = null;
        _animationRepairSubscription?.Dispose();
        _animationRepairSubscription = null;
        _currentTime.OnCompleted();

        Disposables.Dispose();
        _canPaste.Dispose();
        _extensionProvider.Dispose();
        _isElementEditable.Dispose();
        _canReplaceAnimation.Dispose();
        _editViewModel = null!;
        _parentServices = null;
        _propertyEditorControlHost = null;
        _element = null;
        _scene = null;
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
        // TryPasteJson checks the edit it makes; a paste over content that could not be loaded repairs it.
        if (!CanReplaceContent) return false;
        if (PasteFormat is not { } format) return false;
        var clipboard = ClipboardHelper.GetClipboard();
        if (clipboard == null) return false;
        string? json = await CoreObjectClipboard.TryGetJsonAsync(clipboard, format);
        return json != null && TryPasteJson(json);
    }

    public virtual IReadOnlyReactiveProperty<bool> CanSaveAsTemplate => s_alwaysFalse;

    protected virtual Type? TemplateBaseType => null;

    protected virtual ICoreSerializable? GetTemplateTarget() => null;

    public virtual bool ApplyTemplate(ObjectTemplateItem template) => false;

    public bool IsListItemAdapter => PropertyAdapter is IListItemAccessor;

    public virtual bool IsTemplateGroup => false;

    public virtual bool AddTemplateAsListItem(ObjectTemplateItem template)
    {
        if (!IsElementEditable) return false;
        if (PropertyAdapter is not IListItemAccessor accessor) return false;
        if (template.CreateInstance() is not { } instance) return false;

        try
        {
            int insertIndex = Math.Clamp(accessor.Index, 0, accessor.List.Count);
            accessor.List.Insert(insertIndex, instance);
            Commit(CommandNames.ApplyTemplate);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public virtual IEnumerable<ObjectTemplateItem> GetApplicableTemplates()
        => TemplateBaseType is { } t ? ObjectTemplateService.Instance.FindByBaseType(t) : [];

    public virtual string GetTemplateDefaultName()
    {
        if (GetTemplateTarget() is { } target)
            return TypeDisplayHelpers.GetLocalizedName(target.GetType());
        return string.Empty;
    }

    public async ValueTask<bool> SaveAsTemplateAsync(string name)
    {
        if (GetTemplateTarget() is not { } target) return false;
        return await ObjectTemplateService.Instance.AddFromInstanceAsync(target, name) != null;
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
        if (serviceType == typeof(IPropertyEditorControlHost))
            return _propertyEditorControlHost;
        if (serviceType.IsAssignableTo(typeof(IPropertyAdapter)))
            return PropertyAdapter;

        return _parentServices?.GetService(serviceType) ?? _editViewModel?.GetService(serviceType);
    }

    // For editors that build child contexts after Accept. Resolves the ExtensionProvider itself so
    // hosts without an EditViewModel work too.
    protected Beutl.Api.Services.ExtensionProvider GetExtensionProvider()
    {
        return this.GetRequiredService<Beutl.Api.Services.ExtensionProvider>();
    }

    // Fires once Accept has injected the ExtensionProvider.
    protected IObservable<Beutl.Api.Services.ExtensionProvider> ObserveExtensionProvider()
    {
        return _extensionProvider.Where(x => x != null)!;
    }

    public void InvalidateFrameCache()
    {
        if (this.GetService<EditViewModel>() is { Player: { } player, FrameCacheManager.Value: { } cacheManager })
        {
            Task.Run(() =>
            {
                int rate = player.GetFrameRate();
                IEnumerable<Element> elements = _element is { } element ? [element] : [];

                cacheManager.DeleteAndUpdateBlocks(elements
                    .Select(item => FrameCacheRanges.ToFrameRange(item.Range, rate)));
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
        if (!IsElementEditable) return;
        if (GetDefaultValue() is { } defaultValue)
        {
            SetValue(PropertyAdapter.GetValue(), (T?)defaultValue);
        }
    }

    public void SetValue(T? oldValue, T? newValue)
    {
        SetValue(oldValue, newValue, null);
    }

    internal void SetValue(T? oldValue, T? newValue, string? commandName)
    {
        if (!CanReplace(oldValue, newValue)) return;
        if (!EqualityComparer<T>.Default.Equals(oldValue, newValue))
        {
            WriteValue(newValue);

            CompleteElementRepair();
            Commit(commandName);
        }
    }

    public void SetValue(T? newValue)
    {
        // Like SetValue(oldValue, newValue): replacing the current value can repair the element, as when a
        // file-backed editor puts a valid file in place of a source that loading dropped.
        if (IsDisposed || !CanReplace(GetEditingValue(), newValue)) return;
        WriteValue(newValue);

        CompleteElementRepair();
        Commit();
    }

    // The value of the keyframe being edited, or of the property itself when no keyframe is.
    protected internal T? GetEditingValue()
        => EditingKeyFrame.Value is { } keyFrame ? keyFrame.Value : PropertyAdapter.GetValue();

    // Writes to the keyframe being edited, or to the property itself when no keyframe is.
    private void WriteValue(T? newValue)
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
    }

    public T? SetCurrentValueAndGetCoerced(T? value)
    {
        if (IsDisposed) return default;
        if (!IsElementEditable)
            return GetEditingValue();
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
        if (!IsElementEditable) return;
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
        if (IsDisposed || GetAnimation() is not KeyFrameAnimation<T> kfAnimation) return;
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
        if (!IsElementEditable) return;
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
        if (!CanReplace(GetAnimation())) return;
        if (PropertyAdapter is IAnimatablePropertyAdapter<T> animatableProperty)
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

            // Also after a non-animatable property: when loading dropped what could not be read,
            // any replacement repairs the element.
            CompleteElementRepair();
            Commit();
        }

        error = null;
        return true;
    }

    public override void RemoveExpression()
    {
        if (!IsElementEditable) return;
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
