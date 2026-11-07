using System.Collections.Immutable;
using System.Globalization;
using Beutl.Configuration;
using Beutl.Controls.PropertyEditors;
using Beutl.Language;
using Color = Avalonia.Media.Color;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class ColorPaletteServiceTests
{
    private static readonly Color s_red = Color.FromUInt32(0xFFFF0000);
    private static readonly Color s_green = Color.FromUInt32(0xFF00FF00);
    private static readonly Color s_blue = Color.FromUInt32(0xFF0000FF);

    [Test]
    public void The_default_palette_has_each_named_color_once()
    {
        ImmutableArray<PaletteColor> colors = DefaultColorPalette.Palette.Colors;

        Assert.Multiple(() =>
        {
            Assert.That(colors, Has.Length.EqualTo(139));
            Assert.That(colors.Select(c => c.Argb), Is.Unique);
            Assert.That(colors.Select(c => c.Name), Is.Unique);
            Assert.That(colors.Select(c => c.Name), Has.All.Not.Empty);
        });
    }

    [Test]
    public void A_recent_color_moves_to_the_front_and_the_oldest_fall_off()
    {
        var service = new ColorPaletteService(new TestPreferences());

        service.AddRecentColor(s_red);
        service.AddRecentColor(s_green);
        service.AddRecentColor(s_red);
        Assert.That(service.RecentColors, Is.EqualTo(new[] { s_red, s_green }));

        for (uint i = 0; i < ColorPaletteService.MaxRecentColors; i++)
        {
            service.AddRecentColor(Color.FromUInt32(0xFF000000 | i));
        }

        Assert.That(service.RecentColors, Has.Length.EqualTo(ColorPaletteService.MaxRecentColors));
        Assert.That(service.RecentColors[0], Is.EqualTo(Color.FromUInt32(0xFF000000 | (ColorPaletteService.MaxRecentColors - 1))));
        Assert.That(service.RecentColors, Does.Not.Contain(s_red));
    }

    [Test]
    public void The_recent_colors_and_the_users_palettes_outlive_the_service()
    {
        var preferences = new TestPreferences();
        var service = new ColorPaletteService(preferences);
        service.AddRecentColor(s_red);
        service.AddRecentColor(Color.FromUInt32(0x8000FF00));
        ColorPalette palette = new("Brand", [new PaletteColor(0xFF0000FF, "Blue")]);
        service.AddPalette(PaletteScope.User, palette);

        var reloaded = new ColorPaletteService(preferences);

        Assert.That(reloaded.RecentColors, Is.EqualTo(new[] { Color.FromUInt32(0x8000FF00), s_red }));
        Assert.That(reloaded.UserPalettes, Is.EqualTo(new[] { palette }));
    }

    [Test]
    public void Preferences_that_do_not_read_leave_the_palettes_empty()
    {
        var preferences = new TestPreferences();
        preferences.Set(ColorPaletteService.RecentColorsKey, "not json");
        preferences.Set(ColorPaletteService.UserPalettesKey, "{\"Name\": 1}");

        var service = new ColorPaletteService(preferences);

        Assert.That(service.RecentColors, Is.Empty);
        Assert.That(service.UserPalettes, Is.Empty);
    }

    [Test]
    public void A_new_palette_takes_the_first_number_no_palette_has()
    {
        var service = new ColorPaletteService(new TestPreferences());
        var project = new TestProjectHost([new ColorPalette(Name(1), [])]);
        using IDisposable attachment = service.AttachProjectHost(project);
        service.AddPalette(PaletteScope.User, new ColorPalette(Name(2), []));

        Assert.That(service.CreatePaletteName(), Is.EqualTo(Name(3)));

        static string Name(int number)
            => string.Format(CultureInfo.CurrentCulture, Strings.ColorPalette_NewPaletteName, number);
    }

    [Test]
    public void A_palette_moves_into_the_project_and_back()
    {
        var service = new ColorPaletteService(new TestPreferences());
        var project = new TestProjectHost([]);
        using IDisposable attachment = service.AttachProjectHost(project);
        ColorPalette palette = new("Brand", [new PaletteColor(0xFF0000FF)]);
        service.AddPalette(PaletteScope.User, palette);

        Assert.That(service.MovePaletteToScope(PaletteScope.User, 0, PaletteScope.Project), Is.True);
        Assert.That(service.UserPalettes, Is.Empty);
        Assert.That(service.ProjectPalettes, Is.EqualTo(new[] { palette }));

        Assert.That(service.MovePaletteToScope(PaletteScope.Project, 0, PaletteScope.User), Is.True);
        Assert.That(service.UserPalettes, Is.EqualTo(new[] { palette }));
        Assert.That(service.ProjectPalettes, Is.Empty);
    }

    // A move writes where the palette goes first; when the project refuses either write, nothing moves.
    [Test]
    public void A_move_the_project_refuses_leaves_both_scopes_as_they_were()
    {
        var service = new ColorPaletteService(new TestPreferences());
        ColorPalette inProject = new("Project", [new PaletteColor(0xFF00FF00)]);
        var project = new TestProjectHost([inProject]);
        using IDisposable attachment = service.AttachProjectHost(project);
        ColorPalette inUser = new("User", [new PaletteColor(0xFF0000FF)]);
        service.AddPalette(PaletteScope.User, inUser);
        project.Refuses = true;

        Assert.Multiple(() =>
        {
            Assert.That(service.MovePaletteToScope(PaletteScope.User, 0, PaletteScope.Project), Is.False);
            Assert.That(service.MovePaletteToScope(PaletteScope.Project, 0, PaletteScope.User), Is.False);
            Assert.That(service.MoveColor(PaletteScope.User, 0, 0, PaletteScope.Project, 0), Is.False);
            Assert.That(service.UserPalettes, Is.EqualTo(new[] { inUser }));
            Assert.That(service.ProjectPalettes, Is.EqualTo(new[] { inProject }));
        });
    }

    // A move out of the project writes the preferences before the project, so a palette the preferences cannot
    // keep stays in the project instead of being lost on the next start.
    [Test]
    public void A_move_the_preferences_refuse_keeps_the_palette_in_the_project()
    {
        var preferences = new TestPreferences();
        var service = new ColorPaletteService(preferences);
        ColorPalette inProject = new("Project", [new PaletteColor(0xFF00FF00)]);
        var project = new TestProjectHost([inProject]);
        using IDisposable attachment = service.AttachProjectHost(project);
        service.AddPalette(PaletteScope.User, new ColorPalette("User", []));
        preferences.Refuses = true;

        Assert.Multiple(() =>
        {
            Assert.That(service.MovePaletteToScope(PaletteScope.Project, 0, PaletteScope.User), Is.False);
            Assert.That(service.MoveColor(PaletteScope.Project, 0, 0, PaletteScope.User, 0), Is.False);
            Assert.That(service.ProjectPalettes, Is.EqualTo(new[] { inProject }));
            Assert.That(service.UserPalettes, Is.EqualTo(new[] { new ColorPalette("User", []) }));
        });
    }

    [Test]
    public void An_edit_the_preferences_refuse_does_not_apply()
    {
        var preferences = new TestPreferences();
        var service = new ColorPaletteService(preferences);
        service.AddPalette(PaletteScope.User, new ColorPalette("Kept", []));
        preferences.Refuses = true;

        Assert.That(service.AddPalette(PaletteScope.User, new ColorPalette("Lost", [])), Is.False);
        Assert.That(service.EditPalette(PaletteScope.User, 0, p => p.WithName("Renamed")), Is.False);
        Assert.That(service.UserPalettes, Is.EqualTo(new[] { new ColorPalette("Kept", []) }));
        Assert.That(new ColorPaletteService(preferences).UserPalettes, Is.EqualTo(service.UserPalettes),
            "What the service shows is what the preferences hold.");
    }

    [Test]
    public void A_color_moves_between_palettes()
    {
        var service = new ColorPaletteService(new TestPreferences());
        var project = new TestProjectHost([new ColorPalette("Project", [])]);
        using IDisposable attachment = service.AttachProjectHost(project);
        service.AddPalette(PaletteScope.User, new ColorPalette("A", [new PaletteColor(0xFF0000FF, "Blue"), new PaletteColor(0xFFFF0000)]));
        service.AddPalette(PaletteScope.User, new ColorPalette("B", []));

        Assert.That(service.MoveColor(PaletteScope.User, 0, 0, PaletteScope.User, 1), Is.True);
        Assert.That(service.MoveColor(PaletteScope.User, 0, 0, PaletteScope.Project, 0), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(service.UserPalettes, Is.EqualTo(new[]
            {
                new ColorPalette("A", []),
                new ColorPalette("B", [new PaletteColor(0xFF0000FF, "Blue")]),
            }));
            Assert.That(service.ProjectPalettes, Is.EqualTo(new[] { new ColorPalette("Project", [new PaletteColor(0xFFFF0000)]) }));
        });
    }

    // A menu opened before the palettes changed can name a position that is gone.
    [Test]
    public void An_edit_at_a_position_that_is_gone_edits_nothing()
    {
        var service = new ColorPaletteService(new TestPreferences());
        service.AddPalette(PaletteScope.User, new ColorPalette("A", [new PaletteColor(0xFF0000FF)]));

        Assert.Multiple(() =>
        {
            Assert.That(service.EditPalette(PaletteScope.User, 1, p => p.WithName("B")), Is.False);
            Assert.That(service.RemovePalette(PaletteScope.User, 1), Is.False);
            Assert.That(service.MovePalette(PaletteScope.User, 0, 1), Is.False);
            Assert.That(service.MoveColor(PaletteScope.User, 0, 1, PaletteScope.User, 0), Is.False);
            Assert.That(service.AddPalette(PaletteScope.Project, new ColorPalette("C", [])), Is.False,
                "No project is open.");
            Assert.That(service.UserPalettes, Is.EqualTo(new[] { new ColorPalette("A", [new PaletteColor(0xFF0000FF)]) }));
        });
    }

    [Test]
    public void The_project_palettes_come_and_go_with_the_host()
    {
        var service = new ColorPaletteService(new TestPreferences());
        int changes = 0;
        service.Changed += (_, _) => changes++;

        IDisposable attachment = service.AttachProjectHost(new TestProjectHost([new ColorPalette("P", [])]));
        Assert.That(service.ProjectPalettes, Has.Length.EqualTo(1));
        attachment.Dispose();

        Assert.That(service.ProjectPalettes, Is.Null);
        Assert.That(changes, Is.EqualTo(2));
    }

    internal sealed class TestPreferences : IPreferences
    {
        private readonly Dictionary<string, object?> _values = [];

        // Fails each write, as a preferences file that cannot be written does.
        public bool Refuses { get; set; }

        public void Set<T>(string key, T value)
        {
            if (Refuses)
            {
                throw new IOException("The preferences cannot be written.");
            }

            _values[key] = value;
        }

        public T Get<T>(string key, T defaultValue)
            => _values.TryGetValue(key, out object? value) && value is T typed ? typed : defaultValue;
    }

    internal sealed class TestProjectHost(ImmutableArray<ColorPalette> palettes) : IProjectColorPaletteHost
    {
        private ImmutableArray<ColorPalette> _palettes = palettes;

        public bool Refuses { get; set; }

        public ImmutableArray<ColorPalette>? Palettes => _palettes;

        public event EventHandler? PalettesChanged;

        public bool TrySetPalettes(ImmutableArray<ColorPalette> palettes)
        {
            if (Refuses)
            {
                return false;
            }

            _palettes = palettes;
            PalettesChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
    }
}
