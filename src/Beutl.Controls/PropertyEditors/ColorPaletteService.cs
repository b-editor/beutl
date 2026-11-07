using System.Collections.Immutable;
using System.Globalization;
using System.Reactive.Disposables;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Media;
using Beutl.Configuration;
using Beutl.Language;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.Controls.PropertyEditors;

// Where a palette is kept: in the open project's file, or in the preferences, for every project.
internal enum PaletteScope
{
    Project,
    User
}

// The open project's palettes, which the app reads and writes for the controls, which cannot write the
// project file themselves.
internal interface IProjectColorPaletteHost
{
    // The open project's palettes, or null when no project is open.
    ImmutableArray<ColorPalette>? Palettes { get; }

    event EventHandler? PalettesChanged;

    // Writes the palettes into the open project's file. False when no project is open or the write failed,
    // which the host reports to the user.
    bool TrySetPalettes(ImmutableArray<ColorPalette> palettes);
}

// The palettes and the recent colors that the palette tab of every color picker shows. Accessed on the UI thread.
internal sealed class ColorPaletteService
{
    internal const string RecentColorsKey = "ColorPalette.RecentColors";
    internal const string UserPalettesKey = "ColorPalette.UserPalettes";
    internal const int MaxRecentColors = 20;

    private readonly ILogger _logger = Log.CreateLogger<ColorPaletteService>();
    private readonly IPreferences? _preferences;
    private IProjectColorPaletteHost? _projectHost;
    private ImmutableArray<Color> _recentColors;
    private ImmutableArray<ColorPalette> _userPalettes;

    internal ColorPaletteService(IPreferences? preferences)
    {
        _preferences = preferences;
        _recentColors = LoadRecentColors();
        _userPalettes = LoadUserPalettes();
    }

    // Settable so that a test can give the views a service of its own.
    public static ColorPaletteService Instance { get; internal set; } = CreateDefault();

    public event EventHandler? Changed;

    // The colors the pickers applied last, the newest first.
    public ImmutableArray<Color> RecentColors => _recentColors;

    public ImmutableArray<ColorPalette> UserPalettes => _userPalettes;

    // Null when no project is open.
    public ImmutableArray<ColorPalette>? ProjectPalettes => _projectHost?.Palettes;

    // Gives the service the open project's palettes until the returned object is disposed.
    public IDisposable AttachProjectHost(IProjectColorPaletteHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        DetachProjectHost();
        _projectHost = host;
        host.PalettesChanged += OnProjectPalettesChanged;
        RaiseChanged();

        return Disposable.Create(() =>
        {
            if (_projectHost == host)
            {
                DetachProjectHost();
                RaiseChanged();
            }
        });
    }

    public ImmutableArray<ColorPalette> GetPalettes(PaletteScope scope)
    {
        return scope == PaletteScope.Project ? ProjectPalettes ?? [] : _userPalettes;
    }

    public bool IsAvailable(PaletteScope scope)
    {
        return scope == PaletteScope.User || ProjectPalettes.HasValue;
    }

    // Replaces the palettes of a scope. False, and nothing changes, when they could not be written: the project
    // file for the project's palettes, the preferences for the user's.
    public bool SetPalettes(PaletteScope scope, ImmutableArray<ColorPalette> palettes)
    {
        if (scope == PaletteScope.Project)
        {
            // The host raises PalettesChanged once the project holds them.
            return _projectHost?.TrySetPalettes(palettes) == true;
        }

        if (!Save(UserPalettesKey, ColorPalette.ToJson(palettes).ToJsonString()))
        {
            return false;
        }

        _userPalettes = palettes;
        RaiseChanged();
        return true;
    }

    // The edits below take positions in the palettes as they were when the view was built; one that no longer
    // exists, as after the project closed under an open menu, edits nothing.

    public bool EditPalette(PaletteScope scope, int index, Func<ColorPalette, ColorPalette> edit)
    {
        ImmutableArray<ColorPalette> palettes = GetPalettes(scope);
        return IsAvailable(scope) && IsIndex(index, palettes.Length)
            && SetPalettes(scope, palettes.SetItem(index, edit(palettes[index])));
    }

    public bool AddPalette(PaletteScope scope, ColorPalette palette)
    {
        return IsAvailable(scope) && SetPalettes(scope, GetPalettes(scope).Add(palette));
    }

    public bool RemovePalette(PaletteScope scope, int index)
    {
        ImmutableArray<ColorPalette> palettes = GetPalettes(scope);
        return IsAvailable(scope) && IsIndex(index, palettes.Length) && SetPalettes(scope, palettes.RemoveAt(index));
    }

    // Moves a palette to another place among the palettes of its scope.
    public bool MovePalette(PaletteScope scope, int index, int newIndex)
    {
        ImmutableArray<ColorPalette> palettes = GetPalettes(scope);
        return IsAvailable(scope) && IsIndex(index, palettes.Length) && IsIndex(newIndex, palettes.Length)
            && SetPalettes(scope, palettes.RemoveAt(index).Insert(newIndex, palettes[index]));
    }

    // Moves a palette to the end of the palettes of the other scope.
    public bool MovePaletteToScope(PaletteScope scope, int index, PaletteScope newScope)
    {
        ImmutableArray<ColorPalette> palettes = GetPalettes(scope);
        if (scope == newScope || !IsIndex(index, palettes.Length))
        {
            return false;
        }

        ColorPalette palette = palettes[index];
        return Transfer(
            scope, list => list.RemoveAt(index),
            newScope, list => list.Add(palette));
    }

    // Moves a color to the end of another palette, in either scope.
    public bool MoveColor(PaletteScope scope, int palette, int index, PaletteScope newScope, int newPalette)
    {
        ImmutableArray<ColorPalette> source = GetPalettes(scope);
        if (!IsIndex(palette, source.Length) || !IsIndex(index, source[palette].Colors.Length)
            || !IsIndex(newPalette, GetPalettes(newScope).Length)
            || (scope == newScope && palette == newPalette))
        {
            return false;
        }

        PaletteColor color = source[palette].Colors[index];
        if (scope == newScope)
        {
            return IsAvailable(scope) && SetPalettes(scope, MoveWithin(source));
        }

        return Transfer(
            scope, palettes => palettes.SetItem(palette, palettes[palette].RemoveColor(index)),
            newScope, palettes => palettes.SetItem(newPalette, palettes[newPalette].AddColor(color)));

        ImmutableArray<ColorPalette> MoveWithin(ImmutableArray<ColorPalette> palettes)
        {
            palettes = palettes.SetItem(palette, palettes[palette].RemoveColor(index));
            return palettes.SetItem(newPalette, palettes[newPalette].AddColor(color));
        }
    }

    // "Palette 1", "Palette 2" and so on: the first such name that no palette has.
    public string CreatePaletteName()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (ColorPalette palette in _userPalettes.Concat(ProjectPalettes ?? []))
        {
            names.Add(palette.Name);
        }

        for (int i = 1; ; i++)
        {
            string name = string.Format(CultureInfo.CurrentCulture, Strings.ColorPalette_NewPaletteName, i);
            if (!names.Contains(name))
            {
                return name;
            }
        }
    }

    public void AddRecentColor(Color color)
    {
        ImmutableArray<Color> colors = _recentColors.Remove(color).Insert(0, color);
        if (colors.Length > MaxRecentColors)
        {
            colors = colors.RemoveRange(MaxRecentColors, colors.Length - MaxRecentColors);
        }

        if (!colors.SequenceEqual(_recentColors))
        {
            SetRecentColors(colors);
        }
    }

    public void RemoveRecentColor(Color color)
    {
        SetRecentColors(_recentColors.Remove(color));
    }

    public void ClearRecentColors()
    {
        SetRecentColors([]);
    }

    private static bool IsIndex(int index, int count)
    {
        return index >= 0 && index < count;
    }

    // Avoids poisoning the shared service when the preferences cannot be read.
    private static ColorPaletteService CreateDefault()
    {
        try
        {
            return new ColorPaletteService(Preferences.Default);
        }
        catch (Exception ex)
        {
            Log.CreateLogger<ColorPaletteService>()
                .LogWarning(ex, "Failed to resolve preferences; the palettes will not persist this session");
            return new ColorPaletteService(preferences: null);
        }
    }

    // Moves between scopes, writing the destination before the source, so that a failed write never loses what
    // moves: a failed first write changes nothing, and a failed second one takes the destination back. Should that
    // fail too, the moved part is in both scopes rather than in neither. Without preferences the user's palettes
    // last this session only, so nothing moves out of the project, which would lose it on the next start.
    private bool Transfer(
        PaletteScope from, Func<ImmutableArray<ColorPalette>, ImmutableArray<ColorPalette>> editFrom,
        PaletteScope to, Func<ImmutableArray<ColorPalette>, ImmutableArray<ColorPalette>> editTo)
    {
        if (!IsAvailable(from) || !IsAvailable(to) || (from == PaletteScope.Project && _preferences == null))
        {
            return false;
        }

        ImmutableArray<ColorPalette> previousTarget = GetPalettes(to);
        ImmutableArray<ColorPalette> source = editFrom(GetPalettes(from));
        if (!SetPalettes(to, editTo(previousTarget)))
        {
            return false;
        }

        if (SetPalettes(from, source))
        {
            return true;
        }

        SetPalettes(to, previousTarget);
        return false;
    }

    private void DetachProjectHost()
    {
        if (_projectHost != null)
        {
            _projectHost.PalettesChanged -= OnProjectPalettesChanged;
            _projectHost = null;
        }
    }

    private void OnProjectPalettesChanged(object? sender, EventArgs e)
    {
        RaiseChanged();
    }

    private void SetRecentColors(ImmutableArray<Color> colors)
    {
        _recentColors = colors;
        var array = new JsonArray();
        foreach (Color color in colors)
        {
            array.Add(ColorPalette.FormatArgb(color.ToUInt32()));
        }

        Save(RecentColorsKey, array.ToJsonString());
        RaiseChanged();
    }

    private ImmutableArray<Color> LoadRecentColors()
    {
        if (Load(RecentColorsKey) is not JsonArray array)
        {
            return [];
        }

        ImmutableArray<Color>.Builder colors = ImmutableArray.CreateBuilder<Color>();
        foreach (JsonNode? item in array)
        {
            if (item is JsonValue value && value.TryGetValue(out string? s)
                && ColorPalette.TryParseArgb(s, out uint argb)
                && colors.Count < MaxRecentColors)
            {
                colors.Add(Color.FromUInt32(argb));
            }
        }

        return colors.ToImmutable();
    }

    private ImmutableArray<ColorPalette> LoadUserPalettes()
    {
        return ColorPalette.ReadList(Load(UserPalettesKey));
    }

    private JsonNode? Load(string key)
    {
        string? json = _preferences?.Get<string?>(key, null);
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(json);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "The preference {Key} is not valid JSON; it is read as empty", key);
            return null;
        }
    }

    // False when the preferences could not be written. They can hold a value they failed to write, as
    // DefaultPreferences does, and write it with the next key that saves, so a failed write puts the previous value
    // back. Without preferences the service keeps the palettes for this session only, as CreateDefault says.
    private bool Save(string key, string json)
    {
        if (_preferences == null)
        {
            return true;
        }

        string? previous = _preferences.Get<string?>(key, null);
        try
        {
            _preferences.Set(key, json);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save the preference {Key}", key);
            try
            {
                // Writing it fails too, most likely, but the value is back by then.
                _preferences.Set(key, previous);
            }
            catch (Exception)
            {
            }

            return false;
        }
    }

    private void RaiseChanged()
    {
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
