using System.Text.Json.Nodes;
using Beutl.Audio.Graph.Effects;
using Beutl.Operation;
using Beutl.Services;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;

namespace Beutl.ViewModels.Editors;

/// <summary>
/// ViewModel adapter for the new IAudioEffect interface
/// </summary>
public sealed class AudioEffectViewModelAdapter : ValueEditorViewModel<IAudioEffect?>, IAudioEffectViewModel
{
    public AudioEffectViewModelAdapter(IPropertyAdapter<IAudioEffect?> property)
        : base(property)
    {
        EffectName = Value.Select(v =>
            {
                if (v != null)
                {
                    Type type = v.GetType();
                    return LibraryService.Current.FindItem(type)?.DisplayName ?? type.Name;
                }
                else
                {
                    return "Null";
                }
            })
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        // TODO: Implement IAudioEffectGroup when available
        IsGroup = Value.Select(v => false)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        IsGroupOrNull = Value.Select(v => v == null)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        IsExpanded.SkipWhile(v => !v)
            .Take(1)
            .Subscribe(_ =>
                Value.Subscribe(v =>
                {
                    Properties.Value?.Dispose();
                    Properties.Value = null;
                    if (Group.Value is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                    Group.Value = null;

                    // TODO: Implement when IAudioEffectGroup is available
                    if (false) // v is IAudioEffectGroup group
                    {
                        // TODO: Create proper group handling when IAudioEffectGroup is implemented
                        // For now, just handle single effects
                    }
                    else if (v is AudioDelayEffect effect)
                    {
                        // Create properties editor for the effect
                        Properties.Value = new PropertiesEditorViewModel(effect, (p, m) => m.Browsable && p != AudioDelayEffect.IsEnabledProperty);
                    }
                    else if (v is IAudioEffect audioEffect && audioEffect is CoreObject coreObject)
                    {
                        // Generic handling for other IAudioEffect implementations
                        var isEnabledProp = coreObject.GetType().GetProperty("IsEnabledProperty");
                        Properties.Value = new PropertiesEditorViewModel(coreObject, (p, m) => 
                            m.Browsable && (isEnabledProp == null || p != isEnabledProp.GetValue(null)));
                    }

                    AcceptChild();
                })
                .DisposeWith(Disposables))
            .DisposeWith(Disposables);

        IsEnabled = Value.Select(x =>
            {
                if (x is AudioDelayEffect delayEffect)
                {
                    return delayEffect.GetObservable(AudioDelayEffect.IsEnabledProperty);
                }
                else if (x is CoreObject coreObject)
                {
                    // Try to find IsEnabled property dynamically
                    var prop = coreObject.GetType().GetProperty("IsEnabled");
                    if (prop != null)
                    {
                        return Observable.Return((bool)(prop.GetValue(x) ?? false));
                    }
                }
                return Observable.Return(x?.IsEnabled ?? false);
            })
            .Switch()
            .ToReactiveProperty()
            .DisposeWith(Disposables);

        IsEnabled.Skip(1)
            .Subscribe(v =>
            {
                if (Value.Value is AudioDelayEffect effect)
                {
                    CommandRecorder recorder = this.GetRequiredService<CommandRecorder>();
                    RecordableCommands.Edit(effect, AudioDelayEffect.IsEnabledProperty, v, !v)
                        .WithStoables(GetStorables())
                        .DoAndRecord(recorder);
                }
                else if (Value.Value is CoreObject coreObject)
                {
                    // Try to set IsEnabled property dynamically
                    var prop = coreObject.GetType().GetProperty("IsEnabled");
                    var propDef = coreObject.GetType().GetField("IsEnabledProperty")?.GetValue(null) as CoreProperty;
                    if (prop != null && propDef != null)
                    {
                        CommandRecorder recorder = this.GetRequiredService<CommandRecorder>();
                        RecordableCommands.Edit(coreObject, (CoreProperty<object>)propDef, (object)v, prop.GetValue(coreObject))
                            .WithStoables(GetStorables())
                            .DoAndRecord(recorder);
                    }
                }
            })
            .DisposeWith(Disposables);

        Value.CombineWithPrevious()
            .Select(v => v.OldValue)
            .Where(v => v != null)
            .Subscribe(v => this.GetService<ISupportCloseAnimation>()?.Close(v!))
            .DisposeWith(Disposables);
    }

    public ReadOnlyReactivePropertySlim<string?> EffectName { get; }

    public ReadOnlyReactivePropertySlim<bool> IsGroup { get; }

    public ReadOnlyReactivePropertySlim<bool> IsGroupOrNull { get; }

    public ReactivePropertySlim<bool> IsExpanded { get; } = new();

    public ReactiveProperty<bool> IsEnabled { get; }

    public ReactivePropertySlim<PropertiesEditorViewModel?> Properties { get; } = new();

    public ReactivePropertySlim<object?> Group { get; } = new();

    public object? EffectObject => Value.Value;

    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        AcceptChild();
    }

    private void AcceptChild()
    {
        var visitor = new Visitor(this);
        
        if (Group.Value is IPropertyEditorContext groupContext)
        {
            groupContext.Accept(visitor);
        }

        if (Properties.Value != null)
        {
            foreach (IPropertyEditorContext item in Properties.Value.Properties)
            {
                item.Accept(visitor);
            }
        }
    }

    public void ChangeEffectType(Type type)
    {
        if (Activator.CreateInstance(type) is IAudioEffect instance)
        {
            SetValue(Value.Value, instance);
        }
    }

    public void AddItem(Type type)
    {
        // TODO: Implement when IAudioEffectGroup is available
        // TODO: Implement when IAudioEffectGroup is available
        if (false) // Value.Value is IAudioEffectGroup group
        {
            // && Activator.CreateInstance(type) is IAudioEffect instance
            // Add to group
        }
    }

    public void SetNull()
    {
        SetValue(Value.Value, null);
    }

    public override void ReadFromJson(JsonObject json)
    {
        base.ReadFromJson(json);
        try
        {
            if (json.TryGetPropertyValue(nameof(IsExpanded), out var isExpandedNode)
                && isExpandedNode is JsonValue isExpanded)
            {
                IsExpanded.Value = (bool)isExpanded;
            }
            Properties.Value?.ReadFromJson(json);

            if (Group.Value is IPropertyEditorContext groupContext
                && json.TryGetPropertyValue(nameof(Group), out var groupNode)
                && groupNode is JsonObject group)
            {
                groupContext.ReadFromJson(group);
            }
        }
        catch
        {
        }
    }

    public override void WriteToJson(JsonObject json)
    {
        base.WriteToJson(json);
        try
        {
            json[nameof(IsExpanded)] = IsExpanded.Value;
            Properties.Value?.WriteToJson(json);
            
            if (Group.Value is IPropertyEditorContext groupContext)
            {
                var group = new JsonObject();
                groupContext.WriteToJson(group);
                json[nameof(Group)] = group;
            }
        }
        catch
        {
        }
    }

    private sealed record Visitor(AudioEffectViewModelAdapter Obj) : IServiceProvider, IPropertyEditorContextVisitor
    {
        public object? GetService(Type serviceType)
        {
            return Obj.GetService(serviceType);
        }

        public void Visit(IPropertyEditorContext context)
        {
        }
    }
}