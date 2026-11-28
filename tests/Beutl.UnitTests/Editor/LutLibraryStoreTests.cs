using Beutl.Editor.Components.PropertyEditors.LutLibrary;

namespace Beutl.UnitTests.Editor;

[TestFixture]
public sealed class LutLibraryStoreTests
{
    private string _root = null!;
    private string _library = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Directory.CreateTempSubdirectory("beutl-lut-library-").FullName;
        _library = Path.Combine(_root, "luts");
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_root, recursive: true);

    [Test]
    public void Scan_creates_the_library_folder_and_lists_cube_files_from_it_and_added_folders()
    {
        var store = new LutLibraryStore(_library);

        IReadOnlyList<LutLibraryFile> initially = store.Scan();

        Assert.Multiple(() =>
        {
            Assert.That(Directory.Exists(_library), Is.True);
            Assert.That(initially, Is.Empty);
        });

        string cube = Path.Combine(_library, "warm.cube");
        LutTestFiles.WriteCube(cube);
        File.WriteAllText(Path.Combine(_library, "notes.txt"), "not a lut");
        string nested = Path.Combine(_library, "nested");
        Directory.CreateDirectory(nested);
        LutTestFiles.WriteCube(Path.Combine(nested, "ignored.cube"));
        string extra = Path.Combine(_root, "extra");
        Directory.CreateDirectory(extra);
        string other = Path.Combine(extra, "cool.CUBE");
        LutTestFiles.WriteCube(other);

        Assert.Multiple(() =>
        {
            Assert.That(store.AddFolder(extra), Is.True);
            Assert.That(store.AddFolder(extra), Is.False, "An added folder is not added twice.");
            Assert.That(store.AddFolder(Path.Combine(_root, "missing")), Is.False);
        });

        IReadOnlyList<LutLibraryFile> files = store.Scan();

        Assert.Multiple(() =>
        {
            Assert.That(files.Select(file => file.Path), Is.EquivalentTo(new[] { cube, other }));
            Assert.That(files.All(file => !file.IsError), Is.True);
            Assert.That(files.Select(file => file.Name), Is.EqualTo(new[] { "cool.CUBE", "warm.cube" }));
        });
    }

    [Test]
    public void Scan_reports_a_file_that_is_not_a_lut_as_an_error_entry()
    {
        Directory.CreateDirectory(_library);
        File.WriteAllText(Path.Combine(_library, "broken.cube"), "this is not a lut");

        LutLibraryFile file = new LutLibraryStore(_library).Scan().Single();

        Assert.Multiple(() =>
        {
            Assert.That(file.Name, Is.EqualTo("broken.cube"));
            Assert.That(file.IsError, Is.True);
            Assert.That(file.Error, Is.Not.Null.And.Not.Empty);
        });
    }

    [Test]
    public async Task Search_and_favorites_filter_the_listed_files()
    {
        Directory.CreateDirectory(_library);
        LutTestFiles.WriteCube(Path.Combine(_library, "Warm.cube"));
        LutTestFiles.WriteCube(Path.Combine(_library, "cool.cube"));
        var store = new LutLibraryStore(_library);
        using var viewModel = new LutLibraryViewModel(store, new NoThumbnails());

        await viewModel.LoadAsync();

        Assert.That(viewModel.Items.Select(item => item.Name), Is.EqualTo(new[] { "cool.cube", "Warm.cube" }));
        LutLibraryItemViewModel warm = viewModel.Items.Single(item => item.Name == "Warm.cube");

        viewModel.SearchText = "WARM";
        Assert.That(viewModel.Items.Select(item => item.Name), Is.EqualTo(new[] { "Warm.cube" }));

        viewModel.SearchText = string.Empty;
        viewModel.FavoritesOnly = true;
        Assert.Multiple(() =>
        {
            Assert.That(viewModel.Items, Is.Empty);
            Assert.That(viewModel.HasNoMatches, Is.True);
        });

        viewModel.ToggleFavorite(warm);
        Assert.Multiple(() =>
        {
            Assert.That(viewModel.Items, Is.EqualTo(new[] { warm }));
            Assert.That(store.IsFavorite(warm.Path), Is.True);
        });

        viewModel.FavoritesOnly = false;
        Assert.That(viewModel.Items, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task Choosing_an_entry_records_it_as_recent_and_ignores_error_entries()
    {
        Directory.CreateDirectory(_library);
        LutTestFiles.WriteCube(Path.Combine(_library, "chosen.cube"));
        File.WriteAllText(Path.Combine(_library, "broken.cube"), "this is not a lut");
        var store = new LutLibraryStore(_library);
        using var viewModel = new LutLibraryViewModel(store, new NoThumbnails(), () => true);
        LutLibraryItemViewModel? chosen = null;
        viewModel.ItemChosen += (_, item) => chosen = item;

        await viewModel.LoadAsync();
        LutLibraryItemViewModel error = viewModel.Items.Single(item => item.IsError);
        LutLibraryItemViewModel valid = viewModel.Items.Single(item => !item.IsError);

        viewModel.Choose(error);
        Assert.Multiple(() =>
        {
            Assert.That(chosen, Is.Null);
            Assert.That(store.RecentFiles, Is.Empty);
        });

        viewModel.Choose(valid);
        Assert.Multiple(() =>
        {
            Assert.That(chosen, Is.SameAs(valid));
            Assert.That(store.RecentFiles, Is.EqualTo(new[] { valid.Path }));
            Assert.That(viewModel.RecentItems, Is.EqualTo(new[] { valid }));
        });
    }

    [Test]
    public void A_removed_folder_is_no_longer_scanned_or_persisted()
    {
        Directory.CreateDirectory(_library);
        string extra = Path.Combine(_root, "extra");
        Directory.CreateDirectory(extra);
        string cube = Path.Combine(extra, "extra.cube");
        LutTestFiles.WriteCube(cube);
        var store = new LutLibraryStore(_library);

        Assert.That(store.AddFolder(extra), Is.True);
        Assert.That(store.Scan().Select(file => file.Path), Is.EqualTo(new[] { cube }));
        Assert.That(store.RemoveFolder(extra), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(store.Scan(), Is.Empty);
            Assert.That(new LutLibraryStore(_library).ExtraFolders, Is.Empty);
        });
    }

    [Test]
    public void Settings_round_trip_through_library_json()
    {
        Directory.CreateDirectory(_library);
        string extra = Path.Combine(_root, "extra");
        Directory.CreateDirectory(extra);
        string favorite = Path.Combine(_library, "favorite.cube");
        LutTestFiles.WriteCube(favorite);
        var store = new LutLibraryStore(_library);

        Assert.That(store.AddFolder(extra), Is.True);
        store.SetFavorite(favorite, true);
        for (int i = 0; i < 10; i++)
        {
            store.RecordRecent(Path.Combine(_library, $"recent-{i}.cube"));
        }

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(store.SettingsPath), Is.True);
            Assert.That(store.RecentFiles, Has.Count.EqualTo(8));
            Assert.That(store.RecentFiles[0], Is.EqualTo(Path.Combine(_library, "recent-9.cube")));
        });

        var restored = new LutLibraryStore(_library);

        Assert.Multiple(() =>
        {
            Assert.That(restored.ExtraFolders, Is.EqualTo(new[] { extra }));
            Assert.That(restored.IsFavorite(favorite), Is.True);
            Assert.That(restored.RecentFiles, Is.EqualTo(store.RecentFiles));
        });
    }

    [Test]
    public void A_corrupt_settings_file_is_ignored_and_replaced_on_the_next_change()
    {
        Directory.CreateDirectory(_library);
        File.WriteAllText(Path.Combine(_library, "library.json"), "{ this is not json");
        var store = new LutLibraryStore(_library);

        Assert.That(store.ExtraFolders, Is.Empty);

        string extra = Path.Combine(_root, "extra");
        Directory.CreateDirectory(extra);
        Assert.That(store.AddFolder(extra), Is.True);

        var restored = new LutLibraryStore(_library);
        Assert.That(restored.ExtraFolders, Is.EqualTo(new[] { extra }));
    }

    private sealed class NoThumbnails : ILutThumbnailSource
    {
        public Task<LutThumbnailResult> GetAsync(string path, CancellationToken cancellationToken)
            => Task.FromResult(new LutThumbnailResult(null, null));
    }
}
