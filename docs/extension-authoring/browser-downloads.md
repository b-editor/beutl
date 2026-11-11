# Browser download extension guide

`BrowserDownloadExtension` takes over files that pages in the browser tab offer for download and adds them to the editor in a way of its own. A text-to-speech extension can place the speech a site generated with its character and caption, for example, instead of as a plain audio element. The public contract lives in Beutl.Extensibility (`src/Beutl.Extensibility/BrowserDownloadExtension.cs`); the host side is in `src/Beutl.Editor.Components/WebBrowserTab` (`BrowserDownloadHandling.cs`, `WebBrowserTabViewModel.ClaimDownload` and `ImportClaimedDownloadAsync`, and `WebBrowserTabView.Downloads.cs`).

## How a download reaches the extension

1. A page offers a file: it links to one, navigates to one, or a script hands one over as a `blob:` or `data:` URL. The browser asks each `BrowserDownloadExtension` in turn, through `Claim`, whether it takes the file over. The first claim wins.
2. If the claim has a `PageScript`, the browser evaluates it in the offering page right away, while the page still shows what produced the file.
3. The browser shows its download prompt. The claim's `Description` takes the place of the file name there, which stays in the tooltip.
4. When the user confirms, the browser saves the file to the project's download folder (`resources/downloads` next to the project file) without showing the download options, records it in the download history, and calls `ImportAsync`.

Nothing is saved or imported until the user confirms in the prompt, so a page cannot add anything on its own. Offers go to extensions only while the editor can add files to a saved scene in a saved project; otherwise the browser asks where to save the file as usual. Retrying a download from the history and the toolbar's download button do not offer the file to extensions.

## Claiming offers

```csharp
using System.Text.Json;
using Beutl.Extensibility;
using Beutl.ProjectSystem;

[Export]
public sealed class SpeechSiteExtension : BrowserDownloadExtension
{
    public override BrowserDownloadClaim? Claim(BrowserDownloadOffer offer)
    {
        if (offer.Page.Host != "speech.example" || Path.GetExtension(offer.SuggestedName) != ".mp3")
            return null;

        return new BrowserDownloadClaim
        {
            PageScript = "JSON.stringify({ text: document.querySelector('#text')?.value ?? '' })",
            Description = "Add as speech with its character",
        };
    }

    public override ValueTask<bool> ImportAsync(BrowserDownloadImportContext context, CancellationToken cancellationToken)
    {
        if (context.EditorContext.GetService(typeof(Scene)) is not Scene scene)
            return ValueTask.FromResult(false);

        string text = context.PageData is { } data
            ? JsonDocument.Parse(data).RootElement.GetProperty("text").GetString() ?? ""
            : "";
        // Build the elements for context.FileName and add them to the scene in one history transaction.
        return ValueTask.FromResult(true);
    }
}
```

- `Claim` runs on the UI thread for every file a page offers, so decide from `BrowserDownloadOffer` alone and return quickly. Return `null` to leave the file to the browser.
- `BrowserDownloadOffer.Source` is the address of the file. A generated file (`IsGenerated`) has no address that can be requested again, so `Source` is the site credited with it, usually the page itself. `Page` is the page on display when the file was offered, and `SuggestedName` the name the page or the server gave the file.
- An exception from `Claim` is logged, and the browser asks the next extension.

## Reading the page

`PageScript` is a JavaScript expression, such as `JSON.stringify({ ... })` or an immediately invoked function, evaluated in the page's main frame. When its value is a string, `PageData` is that string exactly, on every platform: the browser wraps the expression and reads its value back from an envelope of its own, so the difference between WebView2, which returns script values as JSON, and WebKit, which returns strings as they are, never reaches the extension. `PageData` is `null` when the claim had no script, the script failed or its value was not a string, or the page did not answer within five seconds of the user confirming. Statements, such as `const` declarations, belong inside a function the expression invokes.

The script reads the page when the file arrives. A file the browser holds back while the user has not answered an earlier prompt keeps what its own page showed, so read the inputs that produced the file rather than state that changes afterwards. A file that a frame of a page still loading offers belongs to that page, so it is offered to extensions, and its script runs, once that page is on display. Treat `PageData` as input from the page: it is whatever the page's document holds.

## Importing

- `ImportAsync` runs on the UI thread after the file is saved. `FileName` is its full path. Resolve editor services through `EditorContext`, such as `Scene`, `HistoryManager`, and `IEditorClock`, and add everything in one history transaction so the user can undo it as one step.
- Return `false` to have the browser add the file to the timeline as one element, as it does without the extension, for example when the scene has no place for it.
- An exception is shown in the download prompt as a failure to add the saved file; the file stays in the download folder.
- The token is canceled when the user cancels in the prompt. Let the `OperationCanceledException` through: the prompt then reports the download as canceled instead of as a failure to add the file. The prompt stays busy until `ImportAsync` completes, and the browser offers no other download meanwhile, so a dialog that asks the user something is fine.
- The import leases your extension, so uninstalling the package waits until it completes.
