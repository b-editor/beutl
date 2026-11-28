using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Beutl.Controls;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.LibraryThumbnails;
using Beutl.Editor.Components.PropertyEditors.LutLibrary;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.Components.PropertyEditors.Views;
using Beutl.Graphics.Effects;
using Beutl.Language;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.PropertyAdapters;
using Beutl.Serialization;
using Beutl.Services;
using Beutl.Testing.Headless;
using FluentAvalonia.UI.Controls;
using static Beutl.HeadlessUITests.PropertyEditorTestHost;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class LutLibraryFlyoutTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp() => _directory = Directory.CreateTempSubdirectory("beutl-lut-flyout-").FullName;

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, recursive: true);

    [AvaloniaTest]
    public async Task Searching_and_confirming_an_entry_writes_the_file_picker_uri_and_undo_restores()
    {
        string library = Path.Combine(_directory, "luts");
        Directory.CreateDirectory(library);
        string oldPath = Path.Combine(_directory, "old.cube");
        WriteCube(oldPath);
        string chosenPath = Path.Combine(library, "chosen.cube");
        WriteCube(chosenPath, invert: true);
        File.WriteAllText(Path.Combine(library, "broken.cube"), "this is not a lut");

        var effect = new LutEffect();
        var oldSource = new CubeSource();
        oldSource.ReadFrom(UriHelper.CreateFromPath(oldPath));
        effect.Source.Value = oldSource;
        var adapter = new EnginePropertyAdapter<CubeSource?>(effect.Source, effect);
        var (vm, control) = CreateControl(adapter);
        using var viewModel = vm;
        using var history = new HistoryScope(effect);
        vm.Accept(ServiceVisitor.For(history.History));

        var editor = (CubeSourceEditor)control;
        editor.LibraryFolderPathOverride = library;
        var window = new Window { Content = editor, Width = 760, Height = 520 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);

            HeadlessTestHelpers.Click(LibraryButton(editor));
            await HeadlessTestHelpers.WaitUntilAsync(() => editor.PickerViewModel is { Items.Count: 2 });
            LutLibraryPickerViewModel picker = editor.PickerViewModel!;
            LibraryItemPickerFlyoutPresenter presenter = FindPresenter(window);
            AssertToolbar(presenter, showAll: false, addFolder: true);

            window.KeyPress(Key.F, RawInputModifiers.Control, PhysicalKey.F, "f");
            window.KeyRelease(Key.F, RawInputModifiers.Control, PhysicalKey.F, "f");
            HeadlessTestHelpers.Render();
            window.KeyTextInput("chosen");
            await HeadlessTestHelpers.WaitUntilAsync(
                () => picker.SearchText.Value == "chosen" && picker.Items.Count == 1,
                message: "The LUT picker did not search the files.");
            HeadlessTestHelpers.Render(2);

            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            window.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            HeadlessTestHelpers.Render();
            Assert.That(presenter.SelectedItem, Is.SameAs(picker.Items[0]));

            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
            window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
            await HeadlessTestHelpers.WaitUntilAsync(
                () => effect.Source.Value is { } source
                    && source.Uri == UriHelper.CreateFromPath(chosenPath),
                message: "The picker did not apply the chosen LUT.");

            CubeSource source = effect.Source.Value!;
            Assert.Multiple(() =>
            {
                Assert.That(source, Is.Not.SameAs(oldSource));
                Assert.That(source.Uri, Is.EqualTo(UriHelper.CreateFromPath(chosenPath)));
                Assert.That(history.History.UndoCount, Is.EqualTo(1));
            });

            Assert.That(history.History.Undo(), Is.True);
            Assert.That(effect.Source.Value, Is.SameAs(oldSource));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task A_search_highlights_the_match_it_selects()
    {
        string library = Path.Combine(_directory, "luts");
        Directory.CreateDirectory(library);
        WriteCube(Path.Combine(library, "chosen.cube"));
        WriteCube(Path.Combine(library, "other.cube"));

        var effect = new LutEffect();
        var adapter = new EnginePropertyAdapter<CubeSource?>(effect.Source, effect);
        var (vm, control) = CreateControl(adapter);
        using var viewModel = vm;
        var editor = (CubeSourceEditor)control;
        editor.LibraryFolderPathOverride = library;
        var window = new Window { Content = editor, Width = 760, Height = 520 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);

            HeadlessTestHelpers.Click(LibraryButton(editor));
            await HeadlessTestHelpers.WaitUntilAsync(() => editor.PickerViewModel is { Items.Count: 2 });
            LutLibraryPickerViewModel picker = editor.PickerViewModel!;
            LibraryItemPickerFlyoutPresenter presenter = FindPresenter(window);

            window.KeyPress(Key.F, RawInputModifiers.Control, PhysicalKey.F, "f");
            window.KeyRelease(Key.F, RawInputModifiers.Control, PhysicalKey.F, "f");
            HeadlessTestHelpers.Render();
            window.KeyTextInput("chosen");
            await HeadlessTestHelpers.WaitUntilAsync(
                () => picker.Items.Count == 1 && picker.SelectedItem.Value != null,
                message: "The LUT picker did not select its search match.");
            HeadlessTestHelpers.Render(2);

            PinnableLibraryItem match = picker.Items[0];
            ListBoxItem container = presenter.GetVisualDescendants().OfType<ListBoxItem>()
                .Single(item => item.IsEffectivelyVisible && ReferenceEquals(item.DataContext, match));
            Assert.Multiple(() =>
            {
                Assert.That(picker.SelectedItem.Value, Is.SameAs(match));
                Assert.That(presenter.SelectedItem, Is.SameAs(match));
                Assert.That(container.IsSelected, Is.True, "The list does not highlight the selected LUT.");
            });
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task Pinning_an_entry_persists_it_and_reopening_shows_it_pinned()
    {
        string library = Path.Combine(_directory, "luts");
        Directory.CreateDirectory(library);
        string pinnedPath = Path.Combine(library, "favorite.cube");
        WriteCube(pinnedPath);
        WriteCube(Path.Combine(library, "other.cube"));

        var effect = new LutEffect();
        var adapter = new EnginePropertyAdapter<CubeSource?>(effect.Source, effect);
        var (vm, control) = CreateControl(adapter);
        using var viewModel = vm;
        var editor = (CubeSourceEditor)control;
        editor.LibraryFolderPathOverride = library;
        var window = new Window { Content = editor, Width = 760, Height = 520 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);

            HeadlessTestHelpers.Click(LibraryButton(editor));
            await HeadlessTestHelpers.WaitUntilAsync(() => editor.PickerViewModel is { Items.Count: 2 });
            LutLibraryPickerViewModel first = editor.PickerViewModel!;
            PinnableLibraryItem favorite = first.Items.Single(item => item.DisplayName == "favorite");
            LibraryItemPickerFlyoutPresenter presenter = FindPresenter(window);

            ListBoxItem container = presenter.GetVisualDescendants().OfType<ListBoxItem>()
                .Single(item => ReferenceEquals(item.DataContext, favorite));
            HeadlessTestHelpers.Click(container, move: true);
            ToggleButton pin = container.GetVisualDescendants().OfType<ToggleButton>()
                .Single(toggle => toggle.Name == "ToggleButton");
            HeadlessTestHelpers.Click(pin, move: true);
            await HeadlessTestHelpers.WaitUntilAsync(
                () => first.Items.Any(item => item.IsPinned),
                message: "Clicking the pin did not pin the LUT.");

            using (JsonDocument json = JsonDocument.Parse(File.ReadAllText(Path.Combine(library, "library.json"))))
            {
                string[] favorites = json.RootElement.GetProperty("favorites")
                    .EnumerateArray()
                    .Select(element => element.GetString()!)
                    .ToArray();
                Assert.That(favorites, Does.Contain(pinnedPath));
            }

            HeadlessTestHelpers.Click(presenter.GetVisualDescendants().OfType<Button>()
                .Single(button => button.Name == "DismissButton"));
            await HeadlessTestHelpers.WaitUntilAsync(
                () => !editor.IsPickerOpen,
                message: "The picker did not close.");

            HeadlessTestHelpers.Click(LibraryButton(editor));
            await HeadlessTestHelpers.WaitUntilAsync(
                () => editor.PickerViewModel is { Items.Count: 2 } picker
                    && !ReferenceEquals(picker, first)
                    && picker.Items.Any(item => item.IsPinned),
                message: "The reopened picker did not show the pinned LUT.");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task The_effect_picker_keeps_show_all_and_has_no_add_folder_button()
    {
        var host = new Button();
        var window = new Window { Content = host, Width = 760, Height = 520 };
        try
        {
            window.Show();
            var picker = new SelectLibraryItemDialogViewModel(
                TestShell.FontManager,
                TestShell.Library,
                KnownLibraryItemFormats.FilterEffect,
                typeof(FilterEffect));
            _ = LibraryItemPickerHelper.ShowAsync(host, picker, KnownLibraryItemFormats.FilterEffect);
            await picker.SearchTask;
            HeadlessTestHelpers.Render(3);

            AssertToolbar(FindPresenter(window), showAll: true, addFolder: false);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task Adding_a_folder_from_the_LUT_picker_lists_its_files()
    {
        string library = Path.Combine(_directory, "luts");
        Directory.CreateDirectory(library);
        WriteCube(Path.Combine(library, "library.cube"));
        string extra = Path.Combine(_directory, "extra");
        Directory.CreateDirectory(extra);
        WriteCube(Path.Combine(extra, "extra.cube"));

        var effect = new LutEffect();
        var adapter = new EnginePropertyAdapter<CubeSource?>(effect.Source, effect);
        var (vm, control) = CreateControl(adapter);
        using var viewModel = vm;
        var editor = (CubeSourceEditor)control;
        editor.LibraryFolderPathOverride = library;
        editor.FolderPickerOverride = () => Task.FromResult<string?>(extra);
        var window = new Window { Content = editor, Width = 760, Height = 520 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            HeadlessTestHelpers.Click(LibraryButton(editor));
            await HeadlessTestHelpers.WaitUntilAsync(
                () => editor.PickerViewModel is { Items.Count: 1 },
                message: "The LUT picker did not list the library folder.");
            LutLibraryPickerViewModel picker = editor.PickerViewModel!;
            LibraryItemPickerFlyoutPresenter presenter = FindPresenter(window);
            ToggleButton addFolder = presenter.GetVisualDescendants().OfType<ToggleButton>()
                .Single(button => button.Name == "AddFolderButton");
            Assert.That(addFolder.IsEffectivelyVisible, Is.True);

            HeadlessTestHelpers.Click(addFolder);
            await HeadlessTestHelpers.WaitUntilAsync(
                () => picker.Items.Count == 2 && picker.Items.Any(item => item.DisplayName == "extra"),
                message: "The picker did not list the added folder's LUTs.");

            LutLibraryStore store = ((CubeSourceEditorViewModel)vm).GetLibraryStore();
            using JsonDocument json = JsonDocument.Parse(File.ReadAllText(Path.Combine(library, "library.json")));
            Assert.Multiple(() =>
            {
                Assert.That(store.ExtraFolders, Does.Contain(extra));
                Assert.That(
                    json.RootElement.GetProperty("extraFolders").EnumerateArray().Select(element => element.GetString()),
                    Does.Contain(extra));
            });
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public void Library_button_is_disabled_when_the_element_is_locked()
    {
        var effect = new LutEffect();
        var element = new Element { IsLocked = { Value = true } };
        element.AddObject(effect);
        var adapter = new EnginePropertyAdapter<CubeSource?>(effect.Source, effect);
        var (vm, control) = CreateControl(adapter);
        using var viewModel = vm;
        using var history = new HistoryScope(element);
        vm.Accept(ServiceVisitor.For(history.History, element));

        var editor = (CubeSourceEditor)control;
        var window = new Window { Content = editor, Width = 760, Height = 160 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);

            Assert.That(LibraryButton(editor).IsEffectivelyEnabled, Is.False);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public void The_property_menu_lists_the_extra_LUT_folders()
    {
        string library = Path.Combine(_directory, "luts");
        Directory.CreateDirectory(library);
        string extra = Path.Combine(_directory, "extra-luts");
        Directory.CreateDirectory(extra);

        var effect = new LutEffect();
        var adapter = new EnginePropertyAdapter<CubeSource?>(effect.Source, effect);
        var (vm, control) = CreateControl(adapter);
        using var viewModel = vm;
        var editor = (CubeSourceEditor)control;
        editor.LibraryFolderPathOverride = library;
        Assert.That(((CubeSourceEditorViewModel)vm).GetLibraryStore().AddFolder(extra), Is.True);

        var window = new Window { Content = editor, Width = 760, Height = 160 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);

            PropertyEditorMenu menu = editor.GetVisualDescendants().OfType<PropertyEditorMenu>().Single();
            Button button = menu.GetVisualDescendants().OfType<Button>()
                .First(candidate => candidate.ContextFlyout is FAMenuFlyout);
            HeadlessTestHelpers.Click(button);
            HeadlessTestHelpers.Render(2);

            var flyout = (FAMenuFlyout)button.ContextFlyout!;
            FAMenuFlyoutSubItem folders = flyout.Items.OfType<FAMenuFlyoutSubItem>()
                .Single(item => Equals(item.Text, Strings.LutLibraryFolders));
            Assert.Multiple(() =>
            {
                Assert.That(folders.IsVisible, Is.True);
                Assert.That(folders.Items.OfType<FAMenuFlyoutSubItem>().Select(item => item.Text),
                    Does.Contain(extra));
                Assert.That(folders.Items.OfType<FAMenuFlyoutItem>().Select(item => item.Text),
                    Does.Contain(Strings.LutLibraryAddFolder));
            });
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task The_tiles_show_each_LUT_on_the_elements_frame_without_the_edited_effect()
    {
        TestGpu.RequireAnyDevice();
        string library = Path.Combine(_directory, "luts");
        Directory.CreateDirectory(library);
        WriteCube(Path.Combine(library, "identity.cube"));
        string inverted = Path.Combine(library, "inverted.cube");
        WriteCube(inverted, invert: true);

        // The edited effect already inverts: were it captured too, the inverting tile would show the color back.
        var effect = new LutEffect();
        var edited = new CubeSource();
        edited.ReadFrom(UriHelper.CreateFromPath(inverted));
        effect.Source.Value = edited;
        var shape = new Beutl.Graphics.Shapes.RectShape
        {
            Width = { Value = 400 },
            Height = { Value = 300 },
            Fill = { Value = new Beutl.Media.SolidColorBrush(Beutl.Media.Color.FromArgb(255, 200, 60, 30)) },
            FilterEffect = { Value = effect },
        };
        var element = new Element { Length = { Value = MediaTime.FromSeconds(1) } };
        element.AddObject(shape);
        var adapter = new EnginePropertyAdapter<CubeSource?>(effect.Source, effect);
        var (vm, control) = CreateControl(adapter);
        using var viewModel = vm;
        using var history = new HistoryScope(element);
        vm.Accept(ServiceVisitor.For(history.History, element));

        var editor = (CubeSourceEditor)control;
        editor.LibraryFolderPathOverride = library;
        var window = new Window { Content = editor, Width = 760, Height = 520 };
        try
        {
            (WriteableBitmap identityStill, WriteableBitmap invertedStill) = await OpenTilesAsync(window, editor);
            PixelPoint center = CenterOf(identityStill);
            (int R, int G, int B) identityColor = HeadlessTestHelpers.ReadPixel(identityStill, center);
            (int R, int G, int B) invertedColor = HeadlessTestHelpers.ReadPixel(invertedStill, center);
            Assert.Multiple(() =>
            {
                AssertColor(identityColor, (200, 60, 30), "The identity tile does not show the element's frame.");
                AssertColor(invertedColor, (55, 195, 225), "The inverting tile does not invert the element's frame.");
            });
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task Outside_an_element_the_tiles_show_the_LUTs_on_the_sample()
    {
        TestGpu.RequireAnyDevice();
        string library = Path.Combine(_directory, "luts");
        Directory.CreateDirectory(library);
        WriteCube(Path.Combine(library, "identity.cube"));
        WriteCube(Path.Combine(library, "inverted.cube"), invert: true);

        var effect = new LutEffect();
        var adapter = new EnginePropertyAdapter<CubeSource?>(effect.Source, effect);
        var (vm, control) = CreateControl(adapter);
        using var viewModel = vm;
        var editor = (CubeSourceEditor)control;
        editor.LibraryFolderPathOverride = library;
        var window = new Window { Content = editor, Width = 760, Height = 520 };
        try
        {
            (WriteableBitmap identityStill, WriteableBitmap invertedStill) = await OpenTilesAsync(window, editor);
            PixelPoint center = CenterOf(identityStill);
            (int R, int G, int B) sample = HeadlessTestHelpers.ReadPixel(identityStill, center);
            (int R, int G, int B) invertedColor = HeadlessTestHelpers.ReadPixel(invertedStill, center);
            AssertColor(invertedColor, (255 - sample.R, 255 - sample.G, 255 - sample.B),
                "The inverting tile does not invert the sample.");
        }
        finally
        {
            window.Close();
        }
    }

    // Opens the picker in tile view and waits for the stills of identity.cube and inverted.cube.
    private static async Task<(WriteableBitmap Identity, WriteableBitmap Inverted)> OpenTilesAsync(
        Window window, CubeSourceEditor editor)
    {
        window.Show();
        HeadlessTestHelpers.Render(3);
        HeadlessTestHelpers.Click(LibraryButton(editor));
        await HeadlessTestHelpers.WaitUntilAsync(() => editor.PickerViewModel is { Items.Count: 2 });
        LutLibraryPickerViewModel picker = editor.PickerViewModel!;
        picker.ShowTiles.Value = true;
        FindPresenter(window).ShowTiles = true;
        PinnableLibraryItem identity = picker.Items.Single(item => item.DisplayName == "identity");
        PinnableLibraryItem inverted = picker.Items.Single(item => item.DisplayName == "inverted");
        await HeadlessTestHelpers.WaitUntilAsync(
            () =>
            {
                HeadlessTestHelpers.Render();
                return identity.Thumbnail?.Still != null && inverted.Thumbnail?.Still != null;
            },
            TimeSpan.FromSeconds(60),
            "The LUT tiles did not draw.");
        return ((WriteableBitmap)identity.Thumbnail!.Still!, (WriteableBitmap)inverted.Thumbnail!.Still!);
    }

    private static PixelPoint CenterOf(WriteableBitmap bitmap)
        => new(bitmap.PixelSize.Width / 2, bitmap.PixelSize.Height / 2);

    // The LUT runs on 8-bit sRGB values through linear half floats, so a few levels of rounding are allowed.
    private static void AssertColor((int R, int G, int B) actual, (int R, int G, int B) expected, string message)
    {
        const int tolerance = 4;
        Assert.That(
            Math.Abs(actual.R - expected.R) <= tolerance
                && Math.Abs(actual.G - expected.G) <= tolerance
                && Math.Abs(actual.B - expected.B) <= tolerance,
            Is.True,
            $"{message} Expected {expected}, got {actual}.");
    }

    [AvaloniaTest]
    [Explicit("Writes artifacts/pr-images for visual review.")]
    public async Task Capture_the_library_and_effect_pickers()
    {
        string library = Path.Combine(_directory, "luts");
        Directory.CreateDirectory(library);
        WriteCube(Path.Combine(library, "identity.cube"));
        string favorite = Path.Combine(library, "inverted.cube");
        WriteCube(favorite, invert: true);
        File.WriteAllText(Path.Combine(library, "broken.cube"), "this is not a lut");
        File.WriteAllText(
            Path.Combine(library, "library.json"),
            JsonSerializer.Serialize(new
            {
                extraFolders = Array.Empty<string>(),
                favorites = new[] { favorite }
            }));

        string? root = FindRepositoryRoot();
        Assert.That(root, Is.Not.Null, "The repository root was not found.");
        string output = Path.Combine(root!, "artifacts", "pr-images");
        Directory.CreateDirectory(output);

        WriteCube(Path.Combine(library, "warm.cube"), lut: (r, g, b) => (0.15 + (0.85 * r), 0.05 + (0.9 * g), 0.75 * b));

        // A gradient shows how each LUT moves every hue, which a flat color would not.
        var effect = new LutEffect();
        var gradient = new Beutl.Media.LinearGradientBrush
        {
            StartPoint = { Value = Beutl.Graphics.RelativePoint.TopLeft },
            EndPoint = { Value = Beutl.Graphics.RelativePoint.BottomRight },
        };
        gradient.GradientStops.Add(new Beutl.Media.GradientStop(Beutl.Media.Color.FromUInt32(0xFF0EA5E9), 0));
        gradient.GradientStops.Add(new Beutl.Media.GradientStop(Beutl.Media.Color.FromUInt32(0xFFFACC15), 0.5f));
        gradient.GradientStops.Add(new Beutl.Media.GradientStop(Beutl.Media.Color.FromUInt32(0xFFBE185D), 1));
        var shape = new Beutl.Graphics.Shapes.RectShape
        {
            Width = { Value = 1600 },
            Height = { Value = 900 },
            Fill = { Value = gradient },
            FilterEffect = { Value = effect },
        };
        var element = new Element { Length = { Value = MediaTime.FromSeconds(1) } };
        element.AddObject(shape);
        var adapter = new EnginePropertyAdapter<CubeSource?>(effect.Source, effect);
        var (vm, control) = CreateControl(adapter);
        using var viewModel = vm;
        using var history = new HistoryScope(element);
        vm.Accept(ServiceVisitor.For(history.History, element));
        var editor = (CubeSourceEditor)control;
        editor.LibraryFolderPathOverride = library;

        bool previousOverlays = TestAppBuilder.HeadlessOptions.OverlayPopups;
        var window = new Window { Content = editor, Width = 760, Height = 520 };
        var effectWindow = new Window { Content = new Button { Content = "Pick" }, Width = 760, Height = 520 };
        try
        {
            TestAppBuilder.HeadlessOptions.OverlayPopups = false;
            window.Show();
            HeadlessTestHelpers.Render(3);
            HeadlessTestHelpers.Click(LibraryButton(editor));
            await HeadlessTestHelpers.WaitUntilAsync(() => editor.PickerViewModel is { Items.Count: 4 });
            LutLibraryPickerViewModel picker = editor.PickerViewModel!;
            picker.ShowTiles.Value = true;
            HeadlessTestHelpers.Render(3);

            LibraryItemPickerFlyoutPresenter presenter = FindPresenter(window);
            presenter.ShowTiles = true;
            HeadlessTestHelpers.Render(3);
            await HeadlessTestHelpers.WaitUntilAsync(
                () => presenter.GetVisualDescendants().OfType<AnimatedThumbnail>()
                    .Count(thumbnail => thumbnail.Source?.Still != null) >= 3,
                message: "The LUT thumbnails were not ready for the capture.");
            Capture(presenter, "lut-library-tiles.png");

            picker.ShowTiles.Value = false;
            presenter.ShowTiles = false;
            HeadlessTestHelpers.Render(3);
            Capture(presenter, "lut-library-list.png");

            window.Close();

            var host = (Button)effectWindow.Content!;
            effectWindow.Show();
            var effectPicker = new SelectLibraryItemDialogViewModel(
                TestShell.FontManager,
                TestShell.Library,
                KnownLibraryItemFormats.FilterEffect,
                typeof(FilterEffect),
                LibraryThumbnailCapture.ForObject(vm));
            _ = LibraryItemPickerHelper.ShowAsync(host, effectPicker, KnownLibraryItemFormats.FilterEffect);
            await effectPicker.SearchTask;
            effectPicker.ShowTiles.Value = true;
            HeadlessTestHelpers.Render(3);

            presenter = FindPresenter(effectWindow);
            presenter.CanShowTiles = true;
            presenter.ShowTiles = true;
            HeadlessTestHelpers.Render(3);
            ListBox tiles = presenter.GetVisualDescendants().OfType<ListBox>()
                .Single(list => list.Name == "PART_TileListBox");
            await HeadlessTestHelpers.WaitUntilAsync(
                () => tiles.IsEffectivelyVisible && tiles.ItemCount > 0,
                message: "The effect picker did not show its tiles.");
            if (TestGpu.IsAvailable)
            {
                await HeadlessTestHelpers.WaitUntilAsync(
                    () => presenter.GetVisualDescendants().OfType<AnimatedThumbnail>()
                        .Count(thumbnail => thumbnail.Source?.Still != null) >= 4,
                    TimeSpan.FromSeconds(120),
                    "The effect thumbnails were not ready for the capture.");
            }

            Capture(presenter, "effect-picker-tiles.png");
        }
        finally
        {
            window.Close();
            effectWindow.Close();
            TestAppBuilder.HeadlessOptions.OverlayPopups = previousOverlays;
        }

        void Capture(Control presenter, string name)
        {
            HeadlessTestHelpers.Render(3);
            using WriteableBitmap frame = TopLevel.GetTopLevel(presenter)!.CaptureRenderedFrame()
                ?? throw new AssertionException("No headless frame was rendered.");
            frame.Save(Path.Combine(output, name), PngBitmapEncoderOptions.Default);
        }
    }

    private static Button LibraryButton(CubeSourceEditor editor)
        => editor.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "LibraryButton");

    private static void AssertToolbar(LibraryItemPickerFlyoutPresenter presenter, bool showAll, bool addFolder)
    {
        ToggleButton showAllButton = presenter.GetVisualDescendants().OfType<ToggleButton>()
            .Single(button => button.Name == "ShowAllButton");
        ToggleButton addFolderButton = presenter.GetVisualDescendants().OfType<ToggleButton>()
            .Single(button => button.Name == "AddFolderButton");
        Assert.Multiple(() =>
        {
            Assert.That(showAllButton.IsEffectivelyVisible, Is.EqualTo(showAll), "ShowAll visibility");
            Assert.That(addFolderButton.IsEffectivelyVisible, Is.EqualTo(addFolder), "Add folder visibility");
        });
    }

    private static LibraryItemPickerFlyoutPresenter FindPresenter(Window window)
    {
        LibraryItemPickerFlyoutPresenter? presenter = window.GetVisualDescendants()
            .OfType<LibraryItemPickerFlyoutPresenter>()
            .LastOrDefault();
        if (presenter != null)
            return presenter;

        foreach (Popup popup in window.OpenedPopups)
        {
            if (popup.IsOpen && popup.Child is LibraryItemPickerFlyoutPresenter child)
                return child;
        }

        throw new InvalidOperationException("The picker is not open.");
    }

    private static void WriteCube(
        string path, bool invert = false, Func<double, double, double, (double R, double G, double B)>? lut = null)
    {
        var lines = new List<string> { "LUT_3D_SIZE 2" };
        for (int b = 0; b < 2; b++)
        {
            for (int g = 0; g < 2; g++)
            {
                for (int r = 0; r < 2; r++)
                {
                    if (lut != null)
                    {
                        (double outR, double outG, double outB) = lut(r, g, b);
                        lines.Add(FormattableString.Invariant($"{outR} {outG} {outB}"));
                    }
                    else
                    {
                        lines.Add(invert ? $"{1 - r} {1 - g} {1 - b}" : $"{r} {g} {b}");
                    }
                }
            }
        }

        File.WriteAllLines(path, lines);
    }

    private static string? FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "global.json")))
                return directory.FullName;

            directory = directory.Parent;
        }

        return null;
    }
}
