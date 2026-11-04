using System.Text.Json.Nodes;
using Beutl.Animation;
using Beutl.Audio;
using Beutl.Audio.Graph.Nodes;
using Beutl.Composition;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.ProjectSystem;
using Beutl.PropertyAdapters;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.Components.SoundPannerProperties.ViewModels;

public sealed class SoundPannerViewModel : IPropertyEditorContext, IServiceProvider
{
    private readonly IReadOnlyList<IPropertyAdapter> _properties;
    private readonly CompositeDisposable _subscriptions = [];
    private IServiceProvider? _services;
    private IDisposable? _clockSubscription;
    private IDisposable? _layoutSubscription;
    private IDisposable? _editabilitySubscription;
    private Element? _element;
    private Scene? _scene;
    private bool _editorsCreated;

    public SoundPannerViewModel(IReadOnlyList<IPropertyAdapter> properties)
    {
        _properties = properties;
        Sound = (Sound)properties[0].GetEngineProperty()!.GetOwnerObject()!;
        foreach (var property in Sound.Properties)
        {
            bool isPosition = properties.Any(p => ReferenceEquals(p.GetEngineProperty(), property))
                || ReferenceEquals(property, Sound.TimeRange);
            if (!isPosition && typeof(Sound).GetProperty(property.Name) != null) continue;
            Observable.FromEventPattern(h => property.Edited += h, h => property.Edited -= h)
                .Subscribe(_ =>
                {
                    if (isPosition) RefreshValues();
                    else RefreshLayout();
                }).AddTo(_subscriptions);
        }
        foreach (var property in properties.OfType<IAnimatablePropertyAdapter>())
        {
            if (!ReferenceEquals(property.GetEngineProperty(), Sound.Pan)
                && !ReferenceEquals(property.GetEngineProperty(), Sound.SurroundPan)) continue;
            property.ObserveAnimation
                .Select(animation => animation == null
                    ? Observable.ReturnThenNever<IListProperty<IKeyFrame>?>(null)
                    : animation.KeyFrames.CollectionChangedAsObservable()
                        .Select(_ => animation.KeyFrames)
                        .StartWith(animation.KeyFrames))
                .Switch()
                .Subscribe(_ => RefreshValues())
                .AddTo(_subscriptions);
        }
        Sound.AttachedToHierarchy += OnHierarchyChanged;
        Sound.DetachedFromHierarchy += OnHierarchyChanged;
        // Reading the clip layout can open the media file, so a seek does it only when time can change the layout.
        CurrentTime.Subscribe(_ =>
        {
            if (Sound.HasTimeDependentClipLayout) RefreshLayout();
            else RefreshValues();
        }).AddTo(_subscriptions);
        AttachProject();
    }

    public Sound Sound { get; }

    public PropertyEditorExtension Extension => SoundPannerPropertiesExtension.Instance;

    public ReactivePropertySlim<bool> IsExpanded { get; } = new(false);

    public ReactivePropertySlim<MediaTime> CurrentTime { get; } = new();

    public ReactivePropertySlim<SpeakerLayout> Layout { get; } = new(SpeakerLayout.Stereo);

    public ReactivePropertySlim<bool> IsHorizontalOnly { get; } = new(true);

    public ReactivePropertySlim<bool> CanPan { get; } = new();

    public ReactivePropertySlim<bool> CanDrag { get; } = new();

    public ReactivePropertySlim<float> Pan { get; } = new();

    public ReactivePropertySlim<float> SurroundPan { get; } = new();

    public ReactivePropertySlim<IPropertyEditorContext?> PanEditor { get; } = new();

    public ReactivePropertySlim<IPropertyEditorContext?> SurroundPanEditor { get; } = new();

    public ReactivePropertySlim<IPropertyEditorContext?> CenterLevelEditor { get; } = new();

    public ReactivePropertySlim<IPropertyEditorContext?> LfeLevelEditor { get; } = new();

    public object? GetService(Type serviceType) => _services?.GetService(serviceType);

    public void Accept(IPropertyEditorContextVisitor visitor)
    {
        visitor.Visit(this);
        if (visitor is not IServiceProvider services) return;
        _services = services;
        if (!_editorsCreated && services.GetService<IPropertyEditorFactory>() is { } factory)
        {
            PanEditor.Value = Create(nameof(Sound.Pan));
            SurroundPanEditor.Value = Create(nameof(Sound.SurroundPan));
            CenterLevelEditor.Value = Create(nameof(Sound.CenterLevel));
            LfeLevelEditor.Value = Create(nameof(Sound.LfeLevel));
            _editorsCreated = true;

            IPropertyEditorContext? Create(string name)
                => factory.CreateEditor(_properties.Single(p => p.GetEngineProperty()!.Name == name));
        }
        var childVisitor = new ChildVisitor(this);
        PanEditor.Value?.Accept(childVisitor);
        SurroundPanEditor.Value?.Accept(childVisitor);
        CenterLevelEditor.Value?.Accept(childVisitor);
        LfeLevelEditor.Value?.Accept(childVisitor);
        _clockSubscription?.Dispose();
        _clockSubscription = services.GetService<IEditorClock>()?.CurrentTime.Subscribe(t => CurrentTime.Value = t);
        AttachProject();
    }

    private void OnHierarchyChanged(object? sender, HierarchyAttachmentEventArgs e) => AttachProject();

    private void AttachProject()
    {
        _editabilitySubscription?.Dispose();
        _element = Sound.FindHierarchicalParent<Element>() ?? _services?.GetService<Element>();
        _scene = _element?.FindHierarchicalParent<Scene>() ?? _services?.GetService<Scene>();
        _editabilitySubscription = ElementEditability.Observe(_element, _scene).Subscribe(_ => RefreshValues());
        _layoutSubscription?.Dispose();
        var project = Sound.FindHierarchicalParent<Project>();
        _layoutSubscription = project?.ChannelLayout.GetObservable().Subscribe(_ => RefreshLayout());
        // Subscribing replays the current layout, and each refresh can open the media file.
        if (project == null) RefreshLayout();
    }

    private void RefreshLayout()
    {
        var project = Sound.FindHierarchicalParent<Project>();
        Layout.Value = project.GetSpeakerLayout();
        AudioChannelLayout output = project.GetChannelLayout();
        // The short-circuit keeps mono and stereo projects from reading the clip layout.
        IsHorizontalOnly.Value = !PannerNode.PositionsOnto(output)
            || !PannerNode.IsPositionedClip(Sound.GetClipLayout(output, CurrentTime.Value).ChannelCount);
        RefreshValues();
    }

    private void RefreshValues()
    {
        Pan.Value = GetEffectiveValue(Sound.Pan);
        SurroundPan.Value = GetEffectiveValue(Sound.SurroundPan);
        CanPan.Value = Layout.Value != SpeakerLayout.Mono && ElementEditability.IsEditable(_element, _scene);
        CanDrag.Value = CanPan.Value && CanEditProperty(Sound.Pan)
            && (IsHorizontalOnly.Value || CanEditProperty(Sound.SurroundPan));
    }

    private float GetEffectiveValue(IExpressionProperty<float> property)
        => GetEditingKeyFrame(property)?.Value.Value ?? property.Evaluate(new CompositionContext(CurrentTime.Value));

    private static bool CanEditProperty(IExpressionProperty<float> property) => !property.HasExpression;

    private KeyFrame<float>? GetEditingKeyFrame(IExpressionProperty<float> property)
    {
        if (property.HasExpression || property.GetKeyFrameAnimation() is not { } animation
            || animation.KeyFrames.Count == 0) return null;
        var keyFrames = animation.KeyFrames;
        MediaTime time = animation.ToLocal(CurrentTime.Value);
        int index = keyFrames.IndexAt(time);
        return keyFrames[index] as KeyFrame<float>;
    }

    private bool SetPropertyValue(IExpressionProperty<float> property, float value)
    {
        if (!CanEditProperty(property)) return false;
        if (GetEditingKeyFrame(property) is { } keyFrame)
        {
            if (keyFrame.Value.Value == value) return false;
            keyFrame.Value.Value = value;
        }
        else
        {
            if (property.Value == value) return false;
            property.Value = value;
        }
        return true;
    }

    public bool SetPosition(float pan, float surroundPan)
    {
        if (!CanDrag.Value || !ElementEditability.IsEditable(_element, _scene)) return false;
        bool changed = SetPropertyValue(Sound.Pan, Math.Clamp(pan, -100, 100));
        if (!IsHorizontalOnly.Value)
            changed |= SetPropertyValue(Sound.SurroundPan, Math.Clamp(surroundPan, 0, 100));
        return changed;
    }

    public void ResetPosition()
    {
        if (!CanPan.Value || !ElementEditability.IsEditable(_element, _scene)
            || !CanEditProperty(Sound.Pan) || !CanEditProperty(Sound.SurroundPan)) return;
        bool changed = SetPropertyValue(Sound.Pan, 0);
        changed |= SetPropertyValue(Sound.SurroundPan, 0);
        if (changed) Commit();
    }

    public void Commit() => _services?.GetService<HistoryManager>()?.Commit();

    public void ReadFromJson(JsonObject json)
    {
        if (json.TryGetPropertyValueAsJsonValue(nameof(IsExpanded), out bool isExpanded))
            IsExpanded.Value = isExpanded;
    }

    public void WriteToJson(JsonObject json)
    {
        json[nameof(IsExpanded)] = IsExpanded.Value;
    }

    public void Dispose()
    {
        Sound.AttachedToHierarchy -= OnHierarchyChanged;
        Sound.DetachedFromHierarchy -= OnHierarchyChanged;
        _subscriptions.Dispose();
        _layoutSubscription?.Dispose();
        _editabilitySubscription?.Dispose();
        _clockSubscription?.Dispose();
        foreach (var editor in new[] { PanEditor, SurroundPanEditor, CenterLevelEditor, LfeLevelEditor })
        {
            editor.Value?.Dispose();
            editor.Dispose();
        }
        IsExpanded.Dispose();
        CurrentTime.Dispose();
        Layout.Dispose();
        IsHorizontalOnly.Dispose();
        CanPan.Dispose();
        CanDrag.Dispose();
        Pan.Dispose();
        SurroundPan.Dispose();
    }
}
