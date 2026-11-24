using Beutl.Editor.Components.Helpers;
using Beutl.Editor.PropertyEditors;
using Beutl.Editor.Services;
using Beutl.Graphics.Effects;
using Beutl.PropertyAdapters;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.Components.PropertyEditors.ViewModels;

// A list item owns a PuppetPin, while its inline number editor edits just that pin's animated Position.
internal sealed class PuppetPinItemViewModel : PropertyEditorViewModel
{
    public PuppetPinItemViewModel(IPropertyAdapter<PuppetPin?> item) : base(item)
    {
        Position = item.GetObservable().CombineLatest(ObserveExtensionProvider())
            .Select(pair => pair.First is { } pin
                ? this.GetRequiredService<IPropertyEditorFactory>()
                    .CreateListItemEditor(PropertyAdapterFactory.CreateAdapter(pin.Position, pin)) as PropertyEditorViewModel
                : null)
            .DisposePreviousValue()
            .Do(model => model?.Accept(new ChildVisitor(this)))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);
    }

    public ReadOnlyReactivePropertySlim<PropertyEditorViewModel?> Position { get; }

    public override void Reset() => Position.Value?.Reset();

    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        if (visitor is IServiceProvider)
            Position.Value?.Accept(new ChildVisitor(this));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Position.Value?.Dispose();
    }
}
