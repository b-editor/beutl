using System;
using System.IO;
using Beutl.Configuration;

namespace Beutl.UnitTests.Configuration;

public class DefaultPreferencesTests
{
    private static string NewPrefsFile()
    {
        return Path.Combine(ArtifactProvider.GetArtifactDirectory(), "prefs.json");
    }

    [Test]
    public void SetGet_PrimitiveTypes()
    {
        string file = NewPrefsFile();
        if (File.Exists(file)) File.Delete(file);
        var prefs = new DefaultPreferences(file);

        prefs.Set("i", 42);
        prefs.Set("d", 3.14159);
        prefs.Set("b", true);
        var dt = new DateTime(2023, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        prefs.Set("t", dt);
        prefs.Set("s", "hello");

        Assert.That(prefs.Get("i", 0), Is.EqualTo(42));
        Assert.That(prefs.Get("d", 0.0), Is.EqualTo(3.14159));
        Assert.That(prefs.Get("b", false), Is.True);
        Assert.That(prefs.Get("t", DateTime.MinValue), Is.EqualTo(dt));
        Assert.That(prefs.Get("s", string.Empty), Is.EqualTo("hello"));
    }

    [Test]
    public void RemoveAndClear_Works()
    {
        string file = NewPrefsFile();
        if (File.Exists(file)) File.Delete(file);
        var prefs = new DefaultPreferences(file);

        prefs.Set("x", 1);
        Assert.That(prefs.ContainsKey("x"), Is.True);
        prefs.Remove("x");
        Assert.That(prefs.ContainsKey("x"), Is.False);
        Assert.That(prefs.Get("x", -1), Is.EqualTo(-1));

        prefs.Set("a", 10);
        prefs.Set("b", 20);
        prefs.Clear();
        Assert.That(prefs.ContainsKey("a"), Is.False);
        Assert.That(prefs.ContainsKey("b"), Is.False);
    }

    [Test]
    public void Persistence_LoadsSavedValues()
    {
        string file = NewPrefsFile();
        if (File.Exists(file)) File.Delete(file);
        var prefs = new DefaultPreferences(file);

        prefs.Set("i", 7);
        prefs.Set("s", "foo");

        var prefs2 = new DefaultPreferences(file);
        Assert.That(prefs2.Get("i", 0), Is.EqualTo(7));
        Assert.That(prefs2.Get("s", string.Empty), Is.EqualTo("foo"));
    }

    [Test]
    public void Load_InvalidJson_UsesEmpty()
    {
        string file = NewPrefsFile();
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "{invalid json}");

        var prefs = new DefaultPreferences(file);
        Assert.That(prefs.ContainsKey("anything"), Is.False);
        Assert.That(prefs.Get("x", 123), Is.EqualTo(123));
    }

    [Test]
    public void UnsupportedType_Throws()
    {
        string file = NewPrefsFile();
        if (File.Exists(file)) File.Delete(file);
        var prefs = new DefaultPreferences(file);

        Assert.Throws<NotSupportedException>(() => prefs.Set<Guid>("g", Guid.NewGuid()));
        Assert.Throws<NotSupportedException>(() => prefs.Get<Guid>("g", Guid.Empty));
    }
}

