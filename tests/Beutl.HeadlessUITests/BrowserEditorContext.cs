using Beutl.Editor.Components.WebBrowserTab;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Extensibility;
using Beutl.ProjectSystem;
using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

// The editor a browser tab test runs in. Media the tab adds is recorded in Imported, and the browser
// settings host is offered when one is given.
internal sealed class BrowserEditorContext(CoreObject? document = null, IBrowserSettingsHost? settingsHost = null)
    : IEditorContext, IElementAdder
{
    public List<ElementDescription> Imported { get; } = [];

    public Action? OnImport { get; set; }

    public CoreObject Object { get; } = document ?? new Document();

    public EditorExtension Extension => null!;

    public IReactiveProperty<bool> IsEnabled { get; } = new ReactivePropertySlim<bool>(true);

    public IElementSourceHandlerRegistry SourceHandlers { get; } = new ElementSourceHandlerRegistry();

    public T? FindToolTab<T>(Func<T, bool> condition) where T : IToolContext => default;

    public T? FindToolTab<T>() where T : IToolContext => default;

    public bool OpenToolTab(IToolContext item) => false;

    public void CloseToolTab(IToolContext item)
    {
    }

    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(IElementAdder))
            return this;

        return serviceType == typeof(IBrowserSettingsHost) ? settingsHost : null;
    }

    public ValueTask<ElementAddResult> AddAsync(IReadOnlyList<ElementDescription> descriptions, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Imported.AddRange(descriptions);
        OnImport?.Invoke();
        return ValueTask.FromResult(ElementAddResult.Succeeded(descriptions
            .Select(description => new ElementAddItemResult(description, new Element(), [])).ToArray()));
    }

    private sealed class Document : CoreObject;
}
