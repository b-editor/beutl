using Beutl.Graphics.Effects;
using Reactive.Bindings;

namespace Beutl.Editor.Components.Helpers;

internal interface IPreviewPuppetPinEditor
{
    IReadOnlyReactiveProperty<PuppetPin?> SelectedPin { get; }

    void SelectPin(Puppet effect, PuppetPin pin);

    // Returns false if there is no drawable geometry at the playhead.
    Task<bool> PlacePinsAsync(Puppet effect, Func<PuppetPin, bool> addPin, CancellationToken cancellationToken);
}
