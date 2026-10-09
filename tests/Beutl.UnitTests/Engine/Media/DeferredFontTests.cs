using Beutl.Media;

namespace Beutl.UnitTests.Engine.Media;

[TestFixture]
public class DeferredFontTests
{
    [Test]
    public void CatalogAndNearestWeight_LoadOnlyTheRequestedFaceAndKeepRevisionStable()
    {
        _ = TypefaceProvider.Typeface();
        var manager = (FontManager)Activator.CreateInstance(typeof(FontManager), nonPublic: true)!;
        var family = new FontFamily("Roboto");
        int regularOpens = 0;
        int mediumOpens = 0;
        manager.RegisterFont(new Typeface(family), () =>
        {
            regularOpens++;
            return Open("Roboto-Regular.ttf");
        });
        manager.RegisterFont(new Typeface(family, weight: FontWeight.Medium), () =>
        {
            mediumOpens++;
            return Open("Roboto-Medium.ttf");
        });
        long revision = manager.Revision;
        Assert.Multiple(() =>
        {
            Assert.That(manager.FontFamilies, Does.Contain(family));
            Assert.That(manager.IsRegistered(family), Is.True);
            Assert.That(manager.GetTypefaces(family).Select(face => face.Weight),
                Is.EquivalentTo(new[] { FontWeight.Regular, FontWeight.Medium }));
            Assert.That(regularOpens + mediumOpens, Is.Zero);
        });
        var semiBold = new Typeface(family, weight: FontWeight.SemiBold);
        var medium = manager.ResolveSkia(semiBold);
        Assert.Multiple(() =>
        {
            Assert.That(medium.FontWeight, Is.EqualTo((int)FontWeight.Medium));
            Assert.That(manager.ResolveSkia(semiBold), Is.SameAs(medium));
            Assert.That(regularOpens, Is.Zero);
            Assert.That(mediumOpens, Is.EqualTo(1));
            Assert.That(manager.Revision, Is.EqualTo(revision));
        });
        _ = manager.ResolveSkia(new Typeface(family));
        Assert.That(regularOpens, Is.EqualTo(1));
    }

    [Test]
    public void LoadedAndDeferredStyles_RemainAvailableTogether()
    {
        _ = TypefaceProvider.Typeface();
        var manager = (FontManager)Activator.CreateInstance(typeof(FontManager), nonPublic: true)!;
        var family = new FontFamily("Roboto");
        using (Stream regular = Open("Roboto-Regular.ttf"))
            manager.AddFont(regular);
        int opens = 0;
        manager.RegisterFont(new Typeface(family, weight: FontWeight.Medium), () =>
        {
            opens++;
            return Open("Roboto-Medium.ttf");
        });
        _ = manager.ResolveSkia(new Typeface(family));
        Assert.That(opens, Is.Zero);
        var medium = manager.ResolveSkia(new Typeface(family, weight: FontWeight.Medium));
        Assert.Multiple(() =>
        {
            Assert.That(medium.FontWeight, Is.EqualTo((int)FontWeight.Medium));
            Assert.That(manager.GetTypefaces(family), Has.Length.EqualTo(2));
            Assert.That(opens, Is.EqualTo(1));
        });
    }

    [Test]
    public void BundledFace_ReplacesALoadedDuplicateLazilyAndKeepsOtherStyles()
    {
        _ = TypefaceProvider.Typeface();
        var manager = (FontManager)Activator.CreateInstance(typeof(FontManager), nonPublic: true)!;
        var family = new FontFamily("Roboto");
        var regular = new Typeface(family);
        var medium = new Typeface(family, weight: FontWeight.Medium);
        using (Stream stream = Open("Roboto-Regular.ttf"))
            manager.AddFont(stream);
        using (Stream stream = Open("Roboto-Medium.ttf"))
            manager.AddFont(stream);
        var previousNative = manager._fonts[family][regular];
        var previousRender = manager.ResolveSkia(regular);
        var previousMedium = manager.ResolveSkia(medium);
        long previousRevision = manager.Revision;
        int opens = 0;
        manager.RegisterFont(regular, () =>
        {
            opens++;
            return Open("Roboto-Regular.ttf");
        });
        long registeredRevision = manager.Revision;
        Assert.Multiple(() =>
        {
            Assert.That(opens, Is.Zero);
            Assert.That(registeredRevision, Is.GreaterThan(previousRevision));
            Assert.That(manager.GetTypefaces(family), Has.Length.EqualTo(2));
        });
        var bundled = manager.ResolveSkia(regular);
        Assert.Multiple(() =>
        {
            Assert.That(opens, Is.EqualTo(1));
            Assert.That(bundled, Is.Not.SameAs(previousRender));
            Assert.That(manager._fonts[family][regular], Is.Not.SameAs(previousNative));
            Assert.That(previousNative.Handle, Is.Not.EqualTo(IntPtr.Zero));
            Assert.That(previousRender.Handle, Is.Not.EqualTo(IntPtr.Zero));
            Assert.That(manager.ResolveSkia(regular), Is.SameAs(bundled));
            Assert.That(manager.ResolveSkia(medium), Is.SameAs(previousMedium));
            Assert.That(opens, Is.EqualTo(1));
            Assert.That(manager.Revision, Is.EqualTo(registeredRevision));
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public void LoadFailure_UsesDefaultFontWithoutRetrying(bool failOnOpen)
    {
        _ = TypefaceProvider.Typeface();
        var manager = (FontManager)Activator.CreateInstance(typeof(FontManager), nonPublic: true)!;
        var typeface = new Typeface(new FontFamily("Unavailable deferred font"));
        var fallback = manager.ResolveSkia(manager.DefaultTypeface);
        int opens = 0;
        manager.RegisterFont(typeface, () =>
        {
            opens++;
            if (failOnOpen)
                throw new IOException("Font asset is unavailable.");
            var stream = new MemoryStream();
            stream.Dispose();
            return stream;
        });
        long revision = manager.Revision;

        Assert.Multiple(() =>
        {
            Assert.That(manager.TryResolveSkia(typeface, out _), Is.False);
            Assert.That(manager.ResolveSkia(typeface), Is.SameAs(fallback));
            Assert.That(manager.ResolveSkia(typeface), Is.SameAs(fallback));
            Assert.That(opens, Is.EqualTo(1));
            Assert.That(manager.Revision, Is.EqualTo(revision));
        });
    }

    private static Stream Open(string file) => typeof(DeferredFontTests).Assembly
        .GetManifestResourceStream("Beutl.UnitTests.Assets.Font." + file)!;
}
