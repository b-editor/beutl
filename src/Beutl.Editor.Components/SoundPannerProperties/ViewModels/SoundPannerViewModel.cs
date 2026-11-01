using System.Text.Json.Nodes;
using Beutl.Animation;
using Beutl.Audio;
using Beutl.Composition;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Services;
using Beutl.Engine;
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
    private bool _editorsCreated;

    public SoundPannerViewModel(IReadOnlyList<IPropertyAdapter> properties)
    {
        _properties = properties;
        Sound = (Sound)properties[0].GetEngineProperty()!.GetOwnerObject()!;
        foreach (var property in Sound.Properties)
        {
            bool isPosition = properties.Any(p => ReferenceEquals(p.GetEngineProperty(), property))
                || ReferenceEquals(property, Sound.TimeRange);
            Observable.FromEventPattern(h => property.Edited += h, h => property.Edited -= h)
                .Subscribe(_ =>
                {
                    if (isPosition) RefreshValues();
                    else RefreshLayout();
                }).AddTo(_subscriptions);
        }
        Sound.AttachedToHierarchy += OnHierarchyChanged;
        Sound.DetachedFromHierarchy += OnHierarchyChanged;
        CurrentTime.Subscribe(_ => RefreshValues()).AddTo(_subscriptions);
        AttachProject();
    }

    public Sound Sound { get; }

    public PropertyEditorExtension Extension => SoundPannerPropertiesExtension.Instance;

    public ReactivePropertySlim<MediaTime> CurrentTime { get; } = new();

    public ReactivePropertySlim<SpeakerLayout> Layout { get; } = new(SpeakerLayout.Stereo);

    public ReactivePropertySlim<bool> IsHorizontalOnly { get; } = new(true);

    public ReactivePropertySlim<bool> CanPan { get; } = new();

    public ReactivePropertySlim<bool> CanPosition { get; } = new();

    public ReactivePropertySlim<bool> CanDrag { get; } = new();

    public ReactivePropertySlim<float> Pan { get; } = new();

    public ReactivePropertySlim<float> SurroundPan { get; } = new();

    public ReactivePropertySlim<string?> Message { get; } = new();

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
        _layoutSubscription?.Dispose();
        var project = Sound.FindHierarchicalParent<Project>();
        _layoutSubscription = project?.ChannelLayout.GetObservable().Subscribe(_ => RefreshLayout());
        RefreshLayout();
    }

    private void RefreshLayout()
    {
        var project = Sound.FindHierarchicalParent<Project>();
        Layout.Value = project?.ChannelLayout.Value ?? SpeakerLayout.Stereo;
        bool mono = Layout.Value == SpeakerLayout.Mono;
        bool stereo = Layout.Value == SpeakerLayout.Stereo;
        bool multichannel = !mono && !stereo && Sound.GetClipLayout(project.GetChannelLayout()).ChannelCount >= 3;
        IsHorizontalOnly.Value = mono || stereo || multichannel;
        CanPan.Value = !mono;
        CanPosition.Value = !IsHorizontalOnly.Value;
        Message.Value = mono ? AudioStrings.Panner_Mono
            : stereo ? AudioStrings.Panner_Stereo
            : multichannel ? AudioStrings.Panner_Multichannel : null;
        RefreshValues();
    }

    private void RefreshValues()
    {
        var context = new CompositionContext(CurrentTime.Value);
        Pan.Value = Sound.Pan.Evaluate(context);
        SurroundPan.Value = Sound.SurroundPan.Evaluate(context);
        CanDrag.Value = CanPan.Value && CanEditProperty(Sound.Pan)
            && (IsHorizontalOnly.Value || CanEditProperty(Sound.SurroundPan));
    }

    private bool CanEditProperty(IExpressionProperty<float> property)
        => !property.HasExpression && (property.GetKeyFrameAnimation() == null || GetEditingKeyFrame(property) != null);

    private KeyFrame<float>? GetEditingKeyFrame(IExpressionProperty<float> property)
    {
        if (property.GetKeyFrameAnimation() is not { } animation) return null;
        MediaTime time = animation.ToLocal(CurrentTime.Value);
        return animation.KeyFrames.OfType<KeyFrame<float>>().FirstOrDefault(k => k.KeyTime.Value == time);
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
        if (!CanDrag.Value) return false;
        bool changed = SetPropertyValue(Sound.Pan, Math.Clamp(pan, -100, 100));
        if (!IsHorizontalOnly.Value)
            changed |= SetPropertyValue(Sound.SurroundPan, Math.Clamp(surroundPan, 0, 100));
        return changed;
    }

    public void ResetPosition()
    {
        if (!CanPan.Value || !CanEditProperty(Sound.Pan) || !CanEditProperty(Sound.SurroundPan)) return;
        bool changed = SetPropertyValue(Sound.Pan, 0);
        changed |= SetPropertyValue(Sound.SurroundPan, 0);
        if (changed) Commit();
    }

    public void Commit() => _services?.GetService<HistoryManager>()?.Commit();

    public void ReadFromJson(JsonObject json)
    {
    }

    public void WriteToJson(JsonObject json)
    {
    }

    public void Dispose()
    {
        Sound.AttachedToHierarchy -= OnHierarchyChanged;
        Sound.DetachedFromHierarchy -= OnHierarchyChanged;
        _subscriptions.Dispose();
        _layoutSubscription?.Dispose();
        _clockSubscription?.Dispose();
        foreach (var editor in new[] { PanEditor, SurroundPanEditor, CenterLevelEditor, LfeLevelEditor })
        {
            editor.Value?.Dispose();
            editor.Dispose();
        }
        CurrentTime.Dispose();
        Layout.Dispose();
        IsHorizontalOnly.Dispose();
        CanPan.Dispose();
        CanPosition.Dispose();
        CanDrag.Dispose();
        Pan.Dispose();
        SurroundPan.Dispose();
        Message.Dispose();
    }
}
