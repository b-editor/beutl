using Beutl.Configuration;
using Beutl.Editor.Services;
using Beutl.Editor.Services.LibraryThumbnails;
using Beutl.ProjectSystem;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.Editor.Components.LibraryThumbnails;

// What a picker's thumbnails show types on: an effect picker the element it adds to, and a transition picker the
// edge it sets. Each reads the content when it captures, not when it is made. The library's thumbnails show the
// preview's frame through LibraryPreviewCapture.
internal static class LibraryThumbnailCapture
{
    // The element the edited property belongs to, at the playhead, or null outside an element.
    public static Func<CancellationToken, Task<LibraryThumbnailSubject?>>? ForObject(IServiceProvider? editor)
    {
        if (editor?.GetService(typeof(Element)) is not Element element)
            return null;

        var clock = editor.GetService(typeof(IEditorClock)) as IEditorClock;
        var configuration = editor.GetRequiredService<GlobalConfiguration>();
        return cancellationToken => LibraryThumbnailSubject.CaptureElementAsync(
            element, clock?.CurrentTime.Value ?? element.Start.Value, cancellationToken, configuration.EditorConfig, configuration.ProxyStoreConfig);
    }

    // Both sides of an element's edge.
    public static Func<CancellationToken, Task<LibraryThumbnailSubject?>>? ForEdge(Element? element, ElementEdge edge, IServiceProvider editor)
    {
        if (element == null)
            return null;

        var configuration = editor.GetRequiredService<GlobalConfiguration>();
        return cancellationToken => LibraryThumbnailSubject.CaptureEdgeAsync(element, edge, cancellationToken, configuration.EditorConfig, configuration.ProxyStoreConfig);
    }
}
