using System.Text.Json.Nodes;

using Avalonia.Input;
using Beutl.Audio.Effects;
using Beutl.Editor.Components.Helpers;
using Beutl.Engine;
using Beutl.PropertyAdapters;
using Beutl.Serialization;

using Reactive.Bindings;

namespace Beutl.ViewModels.Editors;

public sealed class AudioEffectEditorViewModel : ValueEditorViewModel<AudioEffect?>, IFallbackObjectViewModel
{
    public AudioEffectEditorViewModel(IPropertyAdapter<AudioEffect?> property)
        : base(property)
    {
        CanCopy = Value.Select(v => v is AudioEffect and not FallbackAudioEffect)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        IsFallback = Value.Select(v => v is IFallback)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        ActualTypeName = Value.Select(FallbackHelper.GetTypeName)
            .ToReadOnlyReactivePropertySlim(Strings.Unknown)
            .DisposeWith(Disposables);

        FallbackMessage = Value.Select(FallbackHelper.GetFallbackMessage)
            .ToReadOnlyReactivePropertySlim(MessageStrings.RestoreFailedTypeNotFound)
            .DisposeWith(Disposables);

        FilterName = Value.Select(v => v != null ? TypeDisplayHelpers.GetLocalizedName(v.GetType()) : "Null")
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        IsGroup = Value.Select(v => v is AudioEffectGroup)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        IsGroupOrNull = Value.Select(v => v is AudioEffectGroup || v == null)
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

                    if (v is AudioEffectGroup group)
                    {
                        var prop = new EnginePropertyAdapter<ICoreList<AudioEffect>>(group.Children, group);
                        Group.Value = new ListEditorViewModel<AudioEffect>(prop)
                        {
                            IsExpanded = { Value = true }
                        };
                    }
                    else if (v != null)
                    {
                        Properties.Value = new PropertiesEditorViewModel(v);
                    }

                    AcceptChild();
                })
                .DisposeWith(Disposables))
            .DisposeWith(Disposables);

        IsEnabled = Value.Select(x => x?.GetObservable(EngineObject.IsEnabledProperty) ?? Observable.ReturnThenNever(x?.IsEnabled ?? false))
            .Switch()
            .ToReactiveProperty()
            .DisposeWith(Disposables);

        IsEnabled.Skip(1)
            .Subscribe(v =>
            {
                if (Value.Value is { } effect)
                {
                    effect.IsEnabled = v;
                    Commit();
                }
            })
            .DisposeWith(Disposables);

    }

    public override IReadOnlyReactiveProperty<bool> CanCopy { get; }

    public override IReadOnlyReactiveProperty<bool> CanSaveAsTemplate => CanCopy;

    protected override Type? TemplateBaseType => typeof(AudioEffect);

    protected override DataFormat<string>? PasteFormat => BeutlDataFormats.AudioEffect;

    public ReadOnlyReactivePropertySlim<string?> FilterName { get; }

    public ReadOnlyReactivePropertySlim<bool> IsGroup { get; }

    public ReadOnlyReactivePropertySlim<bool> IsGroupOrNull { get; }

    public ReactivePropertySlim<bool> IsExpanded { get; } = new();

    public ReactiveProperty<bool> IsEnabled { get; }

    public ReactivePropertySlim<PropertiesEditorViewModel?> Properties { get; } = new();

    public ReactivePropertySlim<ListEditorViewModel<AudioEffect>?> Group { get; } = new();

    public IReadOnlyReactiveProperty<bool> IsFallback { get; }

    public IReadOnlyReactiveProperty<string> ActualTypeName { get; }

    public IReadOnlyReactiveProperty<string> FallbackMessage { get; }

    public IObservable<string?> GetJsonString()
    {
        return Value.Select(v =>
        {
            if (v is FallbackAudioEffect { Json: JsonObject json })
            {
                return json.ToJsonString(JsonHelper.SerializerOptions);
            }

            return null;
        });
    }

    public void SetJsonString(string? str)
    {
        string message = MessageStrings.InvalidJson;
        _ = str ?? throw new Exception(message);
        JsonObject json = (JsonNode.Parse(str) as JsonObject) ?? throw new Exception(message);

        Type? type = json.GetDiscriminator();
        AudioEffect? instance = null;
        if (type?.IsAssignableTo(typeof(AudioEffect)) ?? false)
        {
            instance = Activator.CreateInstance(type) as AudioEffect;
        }

        if (instance == null) throw new Exception(message);

        CoreSerializer.PopulateFromJsonObject(instance, type!, json);

        SetValue(Value.Value, instance);
    }

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

    public void ChangeAudioEffect(AudioEffect instance)
    {
        IsExpanded.Value = true;
        SetValue(Value.Value, instance);
    }

    public void ChangeFilterType(Type type)
    {
        if (Activator.CreateInstance(type) is AudioEffect instance)
            ChangeAudioEffect(instance);
    }

    protected override ICoreSerializable? GetCopyTarget()
        => Value.Value is AudioEffect ae and not FallbackAudioEffect ? ae : null;

    protected override ICoreSerializable? GetTemplateTarget() => GetCopyTarget();

    public override bool TryPasteJson(string json)
    {
        if (!CoreObjectClipboard.TryDeserializeJson<AudioEffect>(json, out var pasted)) return false;

        IsExpanded.Value = true;
        if (Value.Value is AudioEffectGroup group)
        {
            group.Children.Add(pasted);
        }
        else if (EditingKeyFrame.Value is { } kf)
        {
            kf.Value = pasted;
        }
        else if (PropertyAdapter is ListItemAccessorImpl<AudioEffect> listItemAccessor)
        {
            listItemAccessor.List.Insert(listItemAccessor.Index, pasted);
        }
        else
        {
            PropertyAdapter.SetValue(pasted);
        }

        Commit(CommandNames.PasteObject);
        return true;
    }

    public override bool IsTemplateGroup => Value.Value is AudioEffectGroup;

    public override bool ApplyTemplate(ObjectTemplateItem template)
    {
        if (template.CreateInstance() is not AudioEffect instance) return false;
        IsExpanded.Value = true;
        if (Value.Value is AudioEffectGroup)
            AddItem(instance);
        else
            ChangeAudioEffect(instance);
        Commit(CommandNames.ApplyTemplate);
        return true;
    }

    public void AddItem(AudioEffect instance)
    {
        if (Value.Value is AudioEffectGroup group)
        {
            IsExpanded.Value = true;
            group.Children.Add(instance);
            Commit();

            if (Group.Value is { } listEditor)
            {
                var addedItem = listEditor.Items.LastOrDefault();
                if (addedItem?.Context is AudioEffectEditorViewModel vm)
                {
                    vm.IsExpanded.Value = true;
                }
            }
        }
    }

    public void AddItem(Type type)
    {
        if (Activator.CreateInstance(type) is AudioEffect instance)
            AddItem(instance);
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

    private sealed record Visitor(AudioEffectEditorViewModel Obj) : IServiceProvider, IPropertyEditorContextVisitor
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
