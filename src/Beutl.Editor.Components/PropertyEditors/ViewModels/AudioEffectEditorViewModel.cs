using Avalonia.Input;
using Beutl.Audio.Effects;
using Beutl.Engine;
using Beutl.PropertyAdapters;
using Reactive.Bindings;

namespace Beutl.Editor.Components.PropertyEditors.ViewModels;

public sealed class AudioEffectEditorViewModel : GroupableEditorViewModel<AudioEffect, AudioEffectGroup, FallbackAudioEffect>
{
    public AudioEffectEditorViewModel(IPropertyAdapter<AudioEffect?> property)
        : base(property)
    {
        FilterName = Value.Select(v => v != null ? TypeDisplayHelpers.GetLocalizedName(v.GetType()) : "Null")
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);
    }

    protected override DataFormat<string>? PasteFormat => BeutlDataFormats.AudioEffect;

    public ReadOnlyReactivePropertySlim<string?> FilterName { get; }

    public void ChangeAudioEffect(AudioEffect instance) => ChangeValue(instance);

    public void ChangeFilterType(Type type)
    {
        if (Activator.CreateInstance(type) is AudioEffect instance)
            ChangeAudioEffect(instance);
    }

    public void AddItem(Type type)
    {
        if (Activator.CreateInstance(type) is AudioEffect instance)
            AddItem(instance);
    }

    protected override IListProperty<AudioEffect> ChildrenOf(AudioEffectGroup group) => group.Children;
}
