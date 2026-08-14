using System.Globalization;
using Beutl.Configuration;
using Beutl.Editor.Components.FileBrowserTab.Services;

namespace Beutl.UnitTests.Editor;

[TestFixture]
public class FileBrowserFavoritesStoreTests
{
    private string _scratch = null!;

    [SetUp]
    public void SetUp()
    {
        _scratch = Path.Combine(Path.GetTempPath(), $"beutl-favorites-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_scratch);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_scratch))
        {
            Directory.Delete(_scratch, true);
        }
    }

    private string NewDirectory(string name)
    {
        string path = Path.Combine(_scratch, name);
        Directory.CreateDirectory(path);
        return path;
    }

    [Test]
    public void Toggle_adds_then_removes_and_persists_every_time()
    {
        var preferences = new FakePreferences();
        var store = new FileBrowserFavoritesStore(preferences);

        store.Toggle("/a");
        store.Toggle("/b");
        store.Toggle("/a");

        Assert.Multiple(() =>
        {
            Assert.That(store.Favorites, Is.EqualTo(new[] { "/b" }));
            Assert.That(store.Contains("/a"), Is.False);
            Assert.That(preferences.SetCount, Is.EqualTo(3));
        });
    }

    [Test]
    public void Two_managers_share_the_same_favorite_list()
    {
        var store = new FileBrowserFavoritesStore(new FakePreferences());
        using var first = new FavoritesManager(store);
        using var second = new FavoritesManager(store);
        int secondChanged = 0;
        second.Changed += () => secondChanged++;

        first.ToggleFavorite("/shared");

        Assert.Multiple(() =>
        {
            Assert.That(first.Favorites, Is.EqualTo(new[] { "/shared" }));
            Assert.That(second.Favorites, Is.EqualTo(new[] { "/shared" }));
            Assert.That(secondChanged, Is.EqualTo(1));
        });
    }

    [Test]
    public void AddRange_persists_once_and_raises_changed_once()
    {
        var preferences = new FakePreferences();
        var store = new FileBrowserFavoritesStore(preferences);
        using var manager = new FavoritesManager(store);
        int changed = 0;
        manager.Changed += () => changed++;

        manager.AddRange(["/a", "/b", "/c"]);

        Assert.Multiple(() =>
        {
            Assert.That(store.Favorites, Is.EqualTo(new[] { "/a", "/b", "/c" }));
            Assert.That(preferences.SetCount, Is.EqualTo(1));
            Assert.That(changed, Is.EqualTo(1));
        });
    }

    [Test]
    public void AddRange_skips_paths_already_present()
    {
        var store = new FileBrowserFavoritesStore(new FakePreferences());

        store.AddRange(["/a", "/b"]);
        store.AddRange(["/b", "/c"]);

        Assert.That(store.Favorites, Is.EqualTo(new[] { "/a", "/b", "/c" }));
    }

    [Test]
    public void AddRange_of_only_known_paths_does_not_persist()
    {
        var preferences = new FakePreferences();
        var store = new FileBrowserFavoritesStore(preferences);
        store.AddRange(["/a"]);
        int before = preferences.SetCount;

        store.AddRange(["/a"]);

        Assert.That(preferences.SetCount, Is.EqualTo(before));
    }

    [Test]
    public void Dispose_unsubscribes_the_manager_from_the_store()
    {
        var store = new FileBrowserFavoritesStore(new FakePreferences());
        var disposed = new FavoritesManager(store);
        using var alive = new FavoritesManager(store);
        int disposedChanged = 0;
        int aliveChanged = 0;
        disposed.Changed += () => disposedChanged++;
        alive.Changed += () => aliveChanged++;

        disposed.Dispose();
        store.Toggle("/after-dispose");

        Assert.Multiple(() =>
        {
            Assert.That(disposedChanged, Is.EqualTo(0));
            Assert.That(aliveChanged, Is.EqualTo(1));
        });
    }

    [Test]
    public void Dispose_leaves_the_shared_list_intact_for_other_managers()
    {
        var store = new FileBrowserFavoritesStore(new FakePreferences());
        var disposed = new FavoritesManager(store);
        using var alive = new FavoritesManager(store);
        disposed.ToggleFavorite("/kept");

        disposed.Dispose();

        using var late = new FavoritesManager(store);
        Assert.Multiple(() =>
        {
            Assert.That(alive.Favorites, Is.EqualTo(new[] { "/kept" }));
            Assert.That(late.Favorites, Is.EqualTo(new[] { "/kept" }));
        });
    }

    [Test]
    public void Load_tolerates_malformed_json()
    {
        var preferences = new FakePreferences();
        preferences.Set("FileBrowser.Favorites", "not json");

        var store = new FileBrowserFavoritesStore(preferences);

        Assert.That(store.Favorites, Is.Empty);
    }

    [Test]
    public void RefreshFavoriteItems_builds_per_manager_instances()
    {
        var store = new FileBrowserFavoritesStore(new FakePreferences());
        string directory = NewDirectory("shared-favorite");
        store.AddRange([directory]);
        using var first = new FavoritesManager(store);
        using var second = new FavoritesManager(store);

        first.RefreshFavoriteItems();
        second.RefreshFavoriteItems();

        Assert.That(first.FavoriteItems, Has.Count.EqualTo(second.FavoriteItems.Count));
        for (int i = 0; i < first.FavoriteItems.Count; i++)
        {
            Assert.That(first.FavoriteItems[i], Is.Not.SameAs(second.FavoriteItems[i]));
        }
    }

    [Test]
    public void Disposing_one_manager_leaves_another_managers_items_usable()
    {
        var store = new FileBrowserFavoritesStore(new FakePreferences());
        string directory = NewDirectory("expandable");
        Directory.CreateDirectory(Path.Combine(directory, "child"));
        store.AddRange([directory]);
        var disposed = new FavoritesManager(store);
        using var alive = new FavoritesManager(store);
        disposed.RefreshFavoriteItems();
        alive.RefreshFavoriteItems();

        disposed.Dispose();

        var item = alive.FavoriteItems.Single(i => i.FullPath == directory);
        item.IsExpanded.Value = true;

        Assert.That(
            item.Children?.Select(c => c.FullPath),
            Is.EqualTo(new[] { Path.Combine(directory, "child") }));
    }

    [Test]
    public void RefreshFavoriteItems_after_dispose_is_a_no_op()
    {
        var store = new FileBrowserFavoritesStore(new FakePreferences());
        var manager = new FavoritesManager(store);
        manager.Dispose();

        manager.RefreshFavoriteItems();

        Assert.That(manager.FavoriteItems, Is.Empty);
    }

    private sealed class FakePreferences : IPreferences
    {
        private readonly Dictionary<string, string> _values = [];

        public int SetCount { get; private set; }

        public bool ContainsKey(string key) => _values.ContainsKey(key);

        public void Remove(string key) => _values.Remove(key);

        public void Clear() => _values.Clear();

        public void Set<T>(string key, T value)
        {
            SetCount++;
            _values[key] = value?.ToString() ?? string.Empty;
        }

        public T Get<T>(string key, T defaultValue)
        {
            return _values.TryGetValue(key, out string? value)
                ? (T)Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture)
                : defaultValue;
        }
    }
}
