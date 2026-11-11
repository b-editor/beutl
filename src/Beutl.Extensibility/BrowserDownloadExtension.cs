namespace Beutl.Extensibility;

/// <summary>
/// A file that a page in the browser tab offers for download, before the user confirms it.
/// </summary>
public sealed record BrowserDownloadOffer
{
    public BrowserDownloadOffer(Uri source, Uri page, string? suggestedName, bool isGenerated)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(page);
        Source = source;
        Page = page;
        SuggestedName = suggestedName;
        IsGenerated = isGenerated;
    }

    /// <summary>
    /// The address of the file. A file a page script generated as a <c>blob:</c> or <c>data:</c> URL has no
    /// address that can be requested again, so this is the site credited with it.
    /// </summary>
    public Uri Source { get; }

    /// <summary>The page on display when the file was offered.</summary>
    public Uri Page { get; }

    /// <summary>The file name the page or the server suggests, if any.</summary>
    public string? SuggestedName { get; }

    /// <summary>Whether a page script generated the file.</summary>
    public bool IsGenerated { get; }
}

/// <summary>
/// What a <see cref="BrowserDownloadExtension"/> asks of the browser for a download it takes over.
/// </summary>
public sealed record BrowserDownloadClaim
{
    /// <summary>
    /// A JavaScript expression the browser evaluates in the offering page as soon as the page offers the file, while
    /// the page still shows what produced it, such as <c>JSON.stringify({ text: ... })</c> or an immediately invoked
    /// function. <see cref="BrowserDownloadImportContext.PageData"/> passes its value on exactly when it is a string,
    /// on every platform. <see langword="null"/> runs nothing.
    /// </summary>
    public string? PageScript { get; init; }

    /// <summary>
    /// Tells the user what confirming the download does, in the browser's download prompt. It takes the place of
    /// the file name there. <see langword="null"/> shows the file name.
    /// </summary>
    public string? Description { get; init; }
}

/// <summary>
/// A download a <see cref="BrowserDownloadExtension"/> took over, saved and ready to add to the editor.
/// </summary>
public sealed class BrowserDownloadImportContext
{
    public BrowserDownloadImportContext(
        IEditorContext editorContext,
        string fileName,
        BrowserDownloadOffer offer,
        string? pageData)
    {
        ArgumentNullException.ThrowIfNull(editorContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(offer);
        EditorContext = editorContext;
        FileName = fileName;
        Offer = offer;
        PageData = pageData;
    }

    /// <summary>The editor that hosts the browser tab.</summary>
    public IEditorContext EditorContext { get; }

    /// <summary>The full path of the saved file, in the project's download folder.</summary>
    public string FileName { get; }

    /// <summary>The offer the extension claimed.</summary>
    public BrowserDownloadOffer Offer { get; }

    /// <summary>
    /// The string <see cref="BrowserDownloadClaim.PageScript"/> evaluated to, exactly as the page made it, or
    /// <see langword="null"/> when the claim had no script, the script failed or its value was not a string, or the
    /// page did not answer in time.
    /// </summary>
    public string? PageData { get; }
}

/// <summary>
/// Takes over files that pages in the browser tab offer for download, to add them to the editor in a way of its
/// own, such as speech from a text-to-speech site placed with its character and caption.
/// </summary>
/// <remarks>
/// <para>
/// A claimed download still waits for the user to confirm it in the browser's download prompt; a page cannot add
/// anything without it. Once confirmed, the browser saves the file to the project's download folder without asking
/// where, and passes it to <see cref="ImportAsync"/> instead of adding it to the timeline as one element.
/// </para>
/// <para>
/// The browser offers a download to extensions only while its editor can add files to a saved scene. When several
/// extensions claim the same offer, the first one registered takes it.
/// </para>
/// </remarks>
public abstract class BrowserDownloadExtension : Extension
{
    /// <summary>
    /// Decides whether this extension takes over <paramref name="offer"/>. Called on the UI thread when a page offers
    /// a file, so return quickly. Return <see langword="null"/> to leave the file to the browser.
    /// </summary>
    public abstract BrowserDownloadClaim? Claim(BrowserDownloadOffer offer);

    /// <summary>
    /// Adds a claimed file to the editor. Called on the UI thread after the file is saved; the package stays loaded
    /// until it completes. Return <see langword="false"/> to have the browser add the file as one element instead.
    /// </summary>
    /// <remarks>
    /// An exception is reported in the browser's download prompt as a failure to add the saved file. The token is
    /// canceled when the user cancels the download in the prompt.
    /// </remarks>
    public abstract ValueTask<bool> ImportAsync(BrowserDownloadImportContext context, CancellationToken cancellationToken);
}
