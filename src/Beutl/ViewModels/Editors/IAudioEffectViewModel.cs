using System.ComponentModel;
using Reactive.Bindings;

namespace Beutl.ViewModels.Editors;

/// <summary>
/// Common interface for audio effect ViewModels that can work with both ISoundEffect and IAudioEffect
/// </summary>
public interface IAudioEffectViewModel : IDisposable
{
    /// <summary>
    /// Gets the display name of the effect
    /// </summary>
    ReadOnlyReactivePropertySlim<string?> EffectName { get; }
    
    /// <summary>
    /// Gets or sets whether the effect is enabled
    /// </summary>
    ReactiveProperty<bool> IsEnabled { get; }
    
    /// <summary>
    /// Gets whether this is a group effect
    /// </summary>
    ReadOnlyReactivePropertySlim<bool> IsGroup { get; }
    
    /// <summary>
    /// Gets whether this is a group effect or null
    /// </summary>
    ReadOnlyReactivePropertySlim<bool> IsGroupOrNull { get; }
    
    /// <summary>
    /// Gets or sets whether the editor is expanded
    /// </summary>
    ReactivePropertySlim<bool> IsExpanded { get; }
    
    /// <summary>
    /// Gets the properties editor ViewModel if this is a single effect
    /// </summary>
    ReactivePropertySlim<PropertiesEditorViewModel?> Properties { get; }
    
    /// <summary>
    /// Gets the group editor ViewModel if this is a group effect
    /// </summary>
    ReactivePropertySlim<object?> Group { get; }
    
    /// <summary>
    /// Gets the underlying effect object (ISoundEffect or IAudioEffect)
    /// </summary>
    object? EffectObject { get; }
    
    /// <summary>
    /// Changes the effect type
    /// </summary>
    void ChangeEffectType(Type type);
    
    /// <summary>
    /// Adds an item to a group effect
    /// </summary>
    void AddItem(Type type);
    
    /// <summary>
    /// Sets the effect to null
    /// </summary>
    void SetNull();
}