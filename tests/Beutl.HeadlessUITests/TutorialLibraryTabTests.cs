using System.Reactive.Disposables;

using Avalonia.Headless.NUnit;

using Beutl.Editor.Components.LibraryTab.ViewModels;
using Beutl.Services.Tutorials;
using Beutl.Testing.Headless;
using Beutl.ViewModels;

namespace Beutl.HeadlessUITests;

[TestFixture]
[NonParallelizable]
public sealed class TutorialLibraryTabTests
{
    // A tutorial opens the library tab on the editor, as the menu does, so its thumbnails show the preview's frame
    // rather than the samples.
    [AvaloniaTest]
    public async Task A_tutorial_opens_the_library_tab_on_the_preview()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync($"tutorial-library-{Guid.NewGuid():N}");
        if (editor.FindToolTab<LibraryTabViewModel>() is { } open)
        {
            editor.CloseToolTab(open);
            HeadlessTestHelpers.Render(5);
        }

        Assert.That(editor.FindToolTab<LibraryTabViewModel>(), Is.Null);
        Assert.That(TutorialHelpers.OpenLibraryTabIfNeeded(TestShell.Editor), Is.True);
        HeadlessTestHelpers.Settle();

        LibraryTabViewModel? library = editor.FindToolTab<LibraryTabViewModel>();
        Assert.That(library, Is.Not.Null);
        using IDisposable preview = library!.AcquirePreview();
        Assert.That(preview, Is.Not.SameAs(Disposable.Empty), "The tab reads the frame the preview draws.");
    }
}
