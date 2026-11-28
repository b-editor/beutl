using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.PropertyEditors.LutLibrary;
using Beutl.Language;

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
        var store = LutLibraryStore.GetShared(_library);

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

        LutLibraryFile file = LutLibraryStore.GetShared(_library).Scan().Single();

        Assert.Multiple(() =>
        {
            Assert.That(file.Name, Is.EqualTo("broken.cube"));
            Assert.That(file.IsError, Is.True);
            Assert.That(file.Error, Is.Not.Null.And.Not.Empty);
        });
    }

    [Test]
    public async Task Search_filters_by_file_name_and_folder_and_pins_order_the_list()
    {
        Directory.CreateDirectory(_library);
        LutTestFiles.WriteCube(Path.Combine(_library, "Warm.cube"));
        string extra = Path.Combine(_root, "extra-folder");
        Directory.CreateDirectory(extra);
        LutTestFiles.WriteCube(Path.Combine(extra, "cool.cube"));
        var store = LutLibraryStore.GetShared(_library);
        Assert.That(store.AddFolder(extra), Is.True);
        using var viewModel = new LutLibraryPickerViewModel(store, new NoThumbnails());

        await viewModel.LoadAsync();

        Assert.That(viewModel.Items.Select(item => item.DisplayName), Is.EqualTo(new[] { "cool", "Warm" }));

        viewModel.SearchText.Value = "WARM";
        viewModel.FlushSearch();
        Assert.That(viewModel.Items.Select(item => item.DisplayName), Is.EqualTo(new[] { "Warm" }));

        viewModel.SearchText.Value = "extra-folder";
        viewModel.FlushSearch();
        Assert.That(viewModel.Items.Select(item => item.DisplayName), Is.EqualTo(new[] { "cool" }));

        viewModel.SearchText.Value = string.Empty;
        viewModel.FlushSearch();
        PinnableLibraryItem warm = viewModel.Items.Single(item => item.DisplayName == "Warm");

        viewModel.Pin(warm);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.Items[0].DisplayName, Is.EqualTo("Warm"));
            Assert.That(viewModel.Items[0].IsPinned, Is.True);
            Assert.That(store.IsFavorite(((LutLibraryPickerItem)warm.UserData).Path), Is.True);
        });

        viewModel.Unpin(viewModel.Items[0]);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.Items[0].DisplayName, Is.EqualTo("cool"));
            Assert.That(store.IsFavorite(((LutLibraryPickerItem)warm.UserData).Path), Is.False);
        });
    }

    [Test]
    public async Task A_pinned_item_is_persisted_and_restored_as_pinned()
    {
        Directory.CreateDirectory(_library);
        LutTestFiles.WriteCube(Path.Combine(_library, "favorite.cube"));
        var store = LutLibraryStore.GetShared(_library);

        using (var first = new LutLibraryPickerViewModel(store, new NoThumbnails()))
        {
            await first.LoadAsync();
            first.Pin(first.Items.Single());
        }

        using var restored = new LutLibraryPickerViewModel(LutLibraryStore.GetShared(_library), new NoThumbnails());
        await restored.LoadAsync();

        Assert.Multiple(() =>
        {
            Assert.That(restored.Items.Single().IsPinned, Is.True);
            Assert.That(File.ReadAllText(store.SettingsPath), Does.Contain("favorite.cube"));
        });
    }

    [Test]
    public async Task An_older_scan_that_finishes_last_does_not_replace_the_newer_files()
    {
        LutLibraryFile[] older = [new(Path.Combine(_library, "older.cube"), "older.cube", false, null)];
        LutLibraryFile[] newer = [new(Path.Combine(_library, "newer.cube"), "newer.cube", false, null)];
        using var firstStarted = new ManualResetEventSlim();
        using var releaseFirst = new ManualResetEventSlim();
        int calls = 0;
        using var viewModel = new LutLibraryPickerViewModel(
            LutLibraryStore.GetShared(_library),
            new NoThumbnails(),
            chooseFolder: null,
            scan: () =>
            {
                if (Interlocked.Increment(ref calls) > 1)
                    return newer;

                firstStarted.Set();
                releaseFirst.Wait(TimeSpan.FromSeconds(10));
                return older;
            });

        Task first = viewModel.LoadAsync();
        Assert.That(firstStarted.Wait(TimeSpan.FromSeconds(10)), Is.True, "The first scan did not start.");
        await viewModel.LoadAsync();
        Assert.That(viewModel.Items.Select(item => item.DisplayName), Is.EqualTo(new[] { "newer" }));

        releaseFirst.Set();
        await first;

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.Items.Select(item => item.DisplayName), Is.EqualTo(new[] { "newer" }));
            Assert.That(viewModel.IsBusy.Value, Is.False);
        });
    }

    [Test]
    public async Task An_unreadable_file_is_listed_with_its_error_and_no_thumbnail()
    {
        Directory.CreateDirectory(_library);
        File.WriteAllText(Path.Combine(_library, "broken.cube"), "this is not a lut");
        using var viewModel = new LutLibraryPickerViewModel(LutLibraryStore.GetShared(_library), new NoThumbnails());

        await viewModel.LoadAsync();

        PinnableLibraryItem item = viewModel.Items.Single();

        Assert.Multiple(() =>
        {
            Assert.That(item.DisplayName, Is.EqualTo("broken"));
            Assert.That(item.Description, Is.EqualTo(Strings.LutLibraryInvalidFile));
            Assert.That(item.Thumbnail, Is.Null);
            Assert.That(item.UserData, Is.EqualTo(new LutLibraryPickerItem(Path.Combine(_library, "broken.cube"), true)));
        });
    }

    [Test]
    public void Stores_for_the_same_folder_share_pins_and_folders()
    {
        Directory.CreateDirectory(_library);
        string favorite = Path.Combine(_library, "favorite.cube");
        LutTestFiles.WriteCube(favorite);
        string extra = Path.Combine(_root, "extra");
        Directory.CreateDirectory(extra);

        LutLibraryStore first = LutLibraryStore.GetShared(_library);
        LutLibraryStore second = LutLibraryStore.GetShared(_library);
        Assert.That(second, Is.SameAs(first));

        first.SetFavorite(favorite, true);
        Assert.That(second.AddFolder(extra), Is.True);

        LutLibraryStore restored = LutLibraryStore.GetShared(_library);
        Assert.Multiple(() =>
        {
            Assert.That(restored.IsFavorite(favorite), Is.True);
            Assert.That(restored.ExtraFolders, Is.EqualTo(new[] { extra }));
            Assert.That(File.ReadAllText(restored.SettingsPath), Does.Contain("favorite.cube"));
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
        var store = LutLibraryStore.GetShared(_library);

        Assert.That(store.AddFolder(extra), Is.True);
        Assert.That(store.Scan().Select(file => file.Path), Is.EqualTo(new[] { cube }));
        Assert.That(store.RemoveFolder(extra), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(store.Scan(), Is.Empty);
            Assert.That(LutLibraryStore.GetShared(_library).ExtraFolders, Is.Empty);
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
        var store = LutLibraryStore.GetShared(_library);

        Assert.That(store.AddFolder(extra), Is.True);
        store.SetFavorite(favorite, true);

        Assert.That(File.Exists(store.SettingsPath), Is.True);

        // The shared store would hand back the same object, so read a fresh one from library.json.
        LutLibraryStore restored = LutLibraryStore.LoadUnsharedForTests(_library);

        Assert.Multiple(() =>
        {
            Assert.That(restored, Is.Not.SameAs(store));
            Assert.That(restored.ExtraFolders, Is.EqualTo(new[] { extra }));
            Assert.That(restored.IsFavorite(favorite), Is.True);
        });
    }

    [Test]
    public void A_corrupt_settings_file_is_ignored_and_replaced_on_the_next_change()
    {
        Directory.CreateDirectory(_library);
        File.WriteAllText(Path.Combine(_library, "library.json"), "{ this is not json");
        var store = LutLibraryStore.GetShared(_library);

        Assert.That(store.ExtraFolders, Is.Empty);

        string extra = Path.Combine(_root, "extra");
        Directory.CreateDirectory(extra);
        Assert.That(store.AddFolder(extra), Is.True);

        var restored = LutLibraryStore.GetShared(_library);
        Assert.That(restored.ExtraFolders, Is.EqualTo(new[] { extra }));
    }

    private sealed class NoThumbnails : ILutThumbnailSource
    {
        public Task<LutThumbnailResult> GetAsync(string path, CancellationToken cancellationToken)
            => Task.FromResult(new LutThumbnailResult(null, null));
    }
}
