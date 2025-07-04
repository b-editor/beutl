using System.Text.Json.Nodes;
using Beutl.Audio.Effects;
using Beutl.Operation;
using Beutl.Services;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;

namespace Beutl.ViewModels.Editors;

/// <summary>
/// Adapter that wraps the existing SoundEffectEditorViewModel to implement IAudioEffectViewModel
/// </summary>
public sealed class SoundEffectViewModelAdapter : IAudioEffectViewModel
{
    private readonly SoundEffectEditorViewModel _innerViewModel;
    private readonly CompositeDisposable _disposables = new();

    public SoundEffectViewModelAdapter(IPropertyAdapter<ISoundEffect?> property)
    {
        _innerViewModel = new SoundEffectEditorViewModel(property);
        
        // Subscribe to inner Group changes and convert to object
        _innerViewModel.Group
            .Subscribe(g => Group.Value = g)
            .DisposeWith(_disposables);
        
        // Note: SoundEffectEditorViewModel doesn't implement INotifyPropertyChanged
    }

    public ReadOnlyReactivePropertySlim<string?> EffectName => _innerViewModel.FilterName;

    public ReactiveProperty<bool> IsEnabled => _innerViewModel.IsEnabled;

    public ReadOnlyReactivePropertySlim<bool> IsGroup => _innerViewModel.IsGroup;

    public ReadOnlyReactivePropertySlim<bool> IsGroupOrNull => _innerViewModel.IsGroupOrNull;

    public ReactivePropertySlim<bool> IsExpanded => _innerViewModel.IsExpanded;

    public ReactivePropertySlim<PropertiesEditorViewModel?> Properties => _innerViewModel.Properties;

    public ReactivePropertySlim<object?> Group { get; } = new();

    public object? EffectObject => _innerViewModel.Value.Value;

    public void ChangeEffectType(Type type)
    {
        _innerViewModel.ChangeFilterType(type);
    }

    public void AddItem(Type type)
    {
        _innerViewModel.AddItem(type);
    }

    public void SetNull()
    {
        _innerViewModel.SetNull();
    }

    public void Accept(IPropertyEditorContextVisitor visitor)
    {
        _innerViewModel.Accept(visitor);
    }

    public void ReadFromJson(JsonObject json)
    {
        _innerViewModel.ReadFromJson(json);
    }

    public void WriteToJson(JsonObject json)
    {
        _innerViewModel.WriteToJson(json);
    }

    public object? GetService(Type serviceType)
    {
        return _innerViewModel.GetService(serviceType);
    }

    public void Dispose()
    {
        _disposables.Dispose();
        _innerViewModel.Dispose();
    }
}