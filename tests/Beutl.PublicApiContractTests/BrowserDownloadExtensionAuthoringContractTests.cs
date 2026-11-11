using Beutl.Extensibility;
using Reactive.Bindings;

namespace Beutl.PublicApiContractTests;

// A package takes over the files a page in the browser tab offers, such as speech from a text-to-speech site.
[TestFixture]
public sealed class BrowserDownloadExtensionAuthoringContractTests
{
    private static readonly Uri s_page = new("https://speech.example/maker");

    [Test]
    public async Task APluginAuthoredExtension_ClaimsAnOfferAndImportsItWithWhatThePageShowed()
    {
        var extension = new PluginSpeechExtension();
        var editor = new Editor();
        var offer = new BrowserDownloadOffer(s_page, s_page, "speech.mp3", isGenerated: true);

        BrowserDownloadClaim? claim = extension.Claim(offer);
        bool imported = await extension.ImportAsync(
            new BrowserDownloadImportContext(editor, "/project/resources/downloads/speech.mp3", offer, """{"text":"hello"}"""),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(claim?.PageScript, Is.EqualTo(PluginSpeechExtension.Script));
            Assert.That(claim?.Description, Is.EqualTo("Add as speech"));
            Assert.That(extension.Claim(new BrowserDownloadOffer(new Uri("https://other.example/a.mp3"),
                new Uri("https://other.example/"), null, isGenerated: false)), Is.Null);
            Assert.That(imported, Is.True);
            Assert.That(extension.Imported, Is.EqualTo(("/project/resources/downloads/speech.mp3", "hello")));
        }
    }

    [Test]
    public void Claims_RunNoScriptAndShowTheFileNameByDefault()
    {
        var claim = new BrowserDownloadClaim();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(claim.PageScript, Is.Null);
            Assert.That(claim.Description, Is.Null);
        }
    }

    [Test]
    public void OffersAndImports_RequireWhatTheyDescribe()
    {
        var offer = new BrowserDownloadOffer(s_page, s_page, null, isGenerated: false);

        using (Assert.EnterMultipleScope())
        {
            Assert.Throws<ArgumentNullException>(() => _ = new BrowserDownloadOffer(null!, s_page, null, false));
            Assert.Throws<ArgumentNullException>(() => _ = new BrowserDownloadOffer(s_page, null!, null, false));
            Assert.Throws<ArgumentNullException>(() => _ = new BrowserDownloadImportContext(null!, "/a.mp3", offer, null));
            Assert.Throws<ArgumentException>(() => _ = new BrowserDownloadImportContext(new Editor(), " ", offer, null));
            Assert.Throws<ArgumentNullException>(() => _ = new BrowserDownloadImportContext(new Editor(), "/a.mp3", null!, null));
        }
    }

    private sealed class PluginSpeechExtension : BrowserDownloadExtension
    {
        public const string Script = "JSON.stringify({text: document.querySelector('#text').value})";

        public (string FileName, string? Text) Imported { get; private set; }

        public override BrowserDownloadClaim? Claim(BrowserDownloadOffer offer)
            => offer.Page.Host == s_page.Host ? new BrowserDownloadClaim { PageScript = Script, Description = "Add as speech" } : null;

        public override ValueTask<bool> ImportAsync(BrowserDownloadImportContext context, CancellationToken cancellationToken)
        {
            string? text = context.PageData is { } data
                ? System.Text.Json.JsonDocument.Parse(data).RootElement.GetProperty("text").GetString()
                : null;
            Imported = (context.FileName, text);
            return ValueTask.FromResult(true);
        }
    }

    private sealed class Editor : IEditorContext
    {
        public CoreObject Object { get; } = new Document();

        public EditorExtension Extension => null!;

        public IReactiveProperty<bool> IsEnabled { get; } = new ReactivePropertySlim<bool>(true);

        public T? FindToolTab<T>(Func<T, bool> condition) where T : IToolContext => default;

        public T? FindToolTab<T>() where T : IToolContext => default;

        public bool OpenToolTab(IToolContext item) => false;

        public void CloseToolTab(IToolContext item)
        {
        }

        public object? GetService(Type serviceType) => null;

        private sealed class Document : CoreObject;
    }
}
