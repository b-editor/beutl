using System.Text.Json.Nodes;

using Beutl.Audio.Effects;
using Beutl.Commands;
using Beutl.Operators.Configure;
using Beutl.Services;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;

using ReactiveUI;

namespace Beutl.ViewModels.Editors;

public sealed class SoundEffectEditorViewModel : ValueEditorViewModel<ISoundEffect?>
{
    public SoundEffectEditorViewModel(IAbstractProperty<ISoundEffect?> property)
        : base(property)
    {
        FilterName = Value.Select(v =>
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

        IsGroup = Value.Select(v => v is SoundEffectGroup)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        IsGroupOrNull = Value.Select(v => v is SoundEffectGroup || v == null)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        IsExpanded.SkipWhile(v => !v)
            .Take(1)
            .Subscribe(_ =>
                Value.Subscribe(v =>
                {
                    Properties.Value?.Dispose();
                    Properties.Value = null;
                    Group.Value?.Dispose();
                    Group.Value = null;

                    if (v is SoundEffectGroup group)
                    {
                        var prop = new CorePropertyImpl<SoundEffects>(SoundEffectGroup.ChildrenProperty, group);
                        Group.Value = new ListEditorViewModel<ISoundEffect>(prop)
                        {
                            IsExpanded = { Value = true }
                        };
                    }
                    else if (v is SoundEffect effect)
                    {
                        Properties.Value = new PropertiesEditorViewModel(effect, (p, m) => m.Browsable && p != SoundEffect.IsEnabledProperty);
                    }

                    AcceptChild();
                })
                .DisposeWith(Disposables))
            .DisposeWith(Disposables);

        IsEnabled = Value.Select(x => (x as CoreObject)?.GetObservable(SoundEffect.IsEnabledProperty) ?? Observable.Return(x?.IsEnabled ?? false))
            .Switch()
            .ToReactiveProperty()
            .DisposeWith(Disposables);

        IsEnabled.Skip(1)
            .Subscribe(v =>
            {
                if (Value.Value is SoundEffect effect)
                {
                    CommandRecorder recorder = this.GetRequiredService<CommandRecorder>();
                    RecordableCommands.Edit(effect, SoundEffect.IsEnabledProperty, v, !v)
                        .WithStoables(GetStorables())
                        .DoAndRecord(recorder);
                }
            })
            .DisposeWith(Disposables);

        Value.CombineWithPrevious()
            .Select(v => v.OldValue)
            .WhereNotNull()
            .Subscribe(v => this.GetService<ISupportCloseAnimation>()?.Close(v))
            .DisposeWith(Disposables);
    }

    public ReadOnlyReactivePropertySlim<string?> FilterName { get; }

    public ReadOnlyReactivePropertySlim<bool> IsGroup { get; }

    public ReadOnlyReactivePropertySlim<bool> IsGroupOrNull { get; }

    public ReactivePropertySlim<bool> IsExpanded { get; } = new();

    public ReactiveProperty<bool> IsEnabled { get; }

    public ReactivePropertySlim<PropertiesEditorViewModel?> Properties { get; } = new();

    public ReactivePropertySlim<ListEditorViewModel<ISoundEffect>?> Group { get; } = new();

    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        AcceptChild();
    }

    private void AcceptChild()
    {
        var visitor = new Visitor(this);
        Group.Value?.Accept(visitor);

        if (Properties.Value != null)
        {
            foreach (IPropertyEditorContext item in Properties.Value.Properties)
            {
                item.Accept(visitor);
            }
        }
    }

    public void ChangeFilterType(Type type)
    {
        if (Activator.CreateInstance(type) is ISoundEffect instance)
        {
            SetValue(Value.Value, instance);
        }
    }

    public void AddItem(Type type)
    {
        if (Value.Value is SoundEffectGroup group
            && Activator.CreateInstance(type) is ISoundEffect instance)
        {
            CommandRecorder recorder = this.GetRequiredService<CommandRecorder>();
            group.Children.BeginRecord<ISoundEffect>()
                .Add(instance)
                .ToCommand(GetStorables())
                .DoAndRecord(recorder);
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

            if (Group.Value != null
                && json.TryGetPropertyValue(nameof(Group), out var groupNode)
                && groupNode is JsonObject group)
            {
                Group.Value.ReadFromJson(group);
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
            if (Group.Value != null)
            {
                var group = new JsonObject();
                Group.Value.WriteToJson(group);
                json[nameof(Group)] = group;
            }
        }
        catch
        {
        }
    }

    private sealed record Visitor(SoundEffectEditorViewModel Obj) : IServiceProvider, IPropertyEditorContextVisitor
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
