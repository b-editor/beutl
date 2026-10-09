using Beutl.Configuration;
using Dock.Model.Inpc.Controls;
using FluentAvalonia.UI.Controls;
using FluentIconSource = FluentIcons.Avalonia.Fluent.FluentIconSource;

namespace Beutl.ViewModels.Dock;

/// <summary>A tool the new-tab page offers.</summary>
/// <param name="IsEnabled"><see langword="false"/> for a single-instance tool that is already open.</param>
/// <param name="IsPinned">Whether the user pinned the tool to the top of the page.</param>
public sealed record NewToolTabItem(
    ToolTabExtension Extension,
    string Header,
    FAIconSource? Icon,
    bool IsEnabled,
    bool IsPinned);

/// <summary>
/// The empty tab the dock add button opens. It lists the tools and turns into the one the user
/// picks, in the same place.
/// </summary>
public sealed class NewToolTabDockable : Tool
{
    // Icon sources are cached so refreshing and filtering do not rebuild one per keystroke.
    private readonly Dictionary<ToolTabExtension, FAIconSource?> _icons = [];
    private readonly Dictionary<ToolTabExtension, string?> _neutralHeaders = [];
    private NewToolTabItem[] _items = [];
    private IReadOnlyList<NewToolTabItem> _pinnedItems = [];
    private IReadOnlyList<NewToolTabItem> _availableItems = [];
    private IReadOnlyList<NewToolTabItem> _openItems = [];
    private bool _hasMatches;
    private bool _showsToolsHeader;
    private string _searchText = string.Empty;

    public NewToolTabDockable()
    {
        Id = $"{nameof(NewToolTabDockable)}#{Guid.NewGuid():N}";
        Title = Strings.NewTab;
        // Fully qualified: the usual `Icon` alias for the enum would be shadowed by this type's
        // own Icon property.
        Icon = new FluentIconSource { Icon = FluentIcons.Common.Icon.Apps };
        CanClose = true;
        CanFloat = true;
        // A pinned tab leaves its dock, so there would be no place for the picked tool to take.
        CanPin = false;
        CanDockAsDocument = false;
    }

    /// <summary>Gets the icon the tab strip shows left of the title.</summary>
    public FAIconSource Icon { get; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? string.Empty))
                ApplyFilter();
        }
    }

    /// <summary>Gets the pinned tools that match <see cref="SearchText"/> and can be opened, in pin order.</summary>
    public IReadOnlyList<NewToolTabItem> PinnedItems
    {
        get => _pinnedItems;
        private set => SetProperty(ref _pinnedItems, value);
    }

    /// <summary>
    /// Gets the other tools that match <see cref="SearchText"/> and can be opened, as of the last
    /// <see cref="Refresh"/>.
    /// </summary>
    public IReadOnlyList<NewToolTabItem> AvailableItems
    {
        get => _availableItems;
        private set => SetProperty(ref _availableItems, value);
    }

    /// <summary>Gets the single-instance tools, pinned or not, that match <see cref="SearchText"/> but are already open.</summary>
    public IReadOnlyList<NewToolTabItem> OpenItems
    {
        get => _openItems;
        private set => SetProperty(ref _openItems, value);
    }

    /// <summary>Gets whether any tool, open or not, matches <see cref="SearchText"/>.</summary>
    public bool HasMatches
    {
        get => _hasMatches;
        private set => SetProperty(ref _hasMatches, value);
    }

    /// <summary>Gets whether the other tools need a heading to set them apart from the pinned ones.</summary>
    public bool ShowsToolsHeader
    {
        get => _showsToolsHeader;
        private set => SetProperty(ref _showsToolsHeader, value);
    }

    /// <summary>
    /// Gets or sets whether the page should take keyboard focus the next time it is shown. Only the
    /// add button sets it, so a tab restored with the layout does not steal the editor's shortcuts.
    /// </summary>
    internal bool FocusOnShow { get; set; }

    /// <summary>Re-reads the available tools, the pins, and which single-instance tools are already open.</summary>
    public void Refresh()
    {
        if (Factory is not BeutlDockFactory factory)
        {
            _items = [];
        }
        else
        {
            CoreList<string> pins = GlobalConfiguration.Instance.ViewConfig.PinnedToolTabs;
            ToolTabExtension[] extensions = factory.EnumerateToolTabExtensions().ToArray();
            // Drop what a removed package left behind, so the page does not keep its tools alive.
            PruneCache(_icons, extensions);
            PruneCache(_neutralHeaders, extensions);
            _items = extensions
                .Select(extension => new NewToolTabItem(
                    extension,
                    extension.Header!,
                    GetIcon(extension),
                    extension.CanMultiple || !factory.IsToolTabOpen(extension),
                    pins.Contains(GetPinKey(extension))))
                .ToArray();
        }

        ApplyFilter();
    }

    /// <summary>Pins <paramref name="extension"/> to the top of every new-tab page, or unpins it.</summary>
    /// <remarks>Pins are a user preference, so they persist across projects.</remarks>
    public void TogglePin(ToolTabExtension extension)
    {
        CoreList<string> pins = GlobalConfiguration.Instance.ViewConfig.PinnedToolTabs;
        string key = GetPinKey(extension);
        if (!pins.Remove(key))
            pins.Add(key);

        Refresh();
    }

    // The layout's tool identifier: it names the assembly, so same-named types from different
    // packages stay apart, and it leaves out the version, so a pin survives a package update.
    internal static string GetPinKey(ToolTabExtension extension)
    {
        return TypeFormat.ToString(extension.GetType());
    }

    /// <summary>
    /// Lets go of the listed tools while no page shows this tab, so a hidden tab does not keep a
    /// removed package's tools alive. The next <see cref="Refresh"/> lists them again.
    /// </summary>
    internal void ReleaseItems()
    {
        _items = [];
        _icons.Clear();
        _neutralHeaders.Clear();
        ApplyFilter();
    }

    internal bool References(ToolTabExtension extension)
    {
        return _items.Any(item => item.Extension == extension)
               || _icons.ContainsKey(extension)
               || _neutralHeaders.ContainsKey(extension);
    }

    private static void PruneCache<T>(Dictionary<ToolTabExtension, T> cache, ToolTabExtension[] registered)
    {
        foreach (ToolTabExtension stale in cache.Keys.Except(registered).ToArray())
        {
            cache.Remove(stale);
        }
    }

    /// <summary>Opens <paramref name="extension"/> in place of this tab.</summary>
    public bool Open(ToolTabExtension extension)
    {
        return Factory is BeutlDockFactory factory && factory.ReplaceNewToolTab(this, extension);
    }

    private FAIconSource? GetIcon(ToolTabExtension extension)
    {
        if (!_icons.TryGetValue(extension, out FAIconSource? icon))
        {
            icon = extension.GetIcon();
            _icons[extension] = icon;
        }

        return icon;
    }

    private void ApplyFilter()
    {
        string query = _searchText.Trim();
        NewToolTabItem[] matches = query.Length == 0
            ? _items
            : _items.Where(item => Matches(item, query)).ToArray();
        CoreList<string> pins = GlobalConfiguration.Instance.ViewConfig.PinnedToolTabs;
        PinnedItems = matches
            .Where(item => item.IsEnabled && item.IsPinned)
            .OrderBy(item => pins.IndexOf(GetPinKey(item.Extension)))
            .ToArray();
        AvailableItems = matches.Where(item => item.IsEnabled && !item.IsPinned).ToArray();
        OpenItems = matches.Where(item => !item.IsEnabled).ToArray();
        HasMatches = matches.Length > 0;
        ShowsToolsHeader = PinnedItems.Count > 0 && AvailableItems.Count > 0;
    }

    // The English label and the internal name are searched too, so an English name finds a tool in
    // any UI language.
    private bool Matches(NewToolTabItem item, string query)
    {
        return item.Header.Contains(query, StringComparison.CurrentCultureIgnoreCase)
               || item.Extension.DisplayName.Contains(query, StringComparison.CurrentCultureIgnoreCase)
               || GetNeutralHeader(item.Extension)?.Contains(query, StringComparison.CurrentCultureIgnoreCase) == true
               || item.Extension.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase);
    }

    // Header reads localized resources for the current UI culture; the invariant culture resolves
    // them to the neutral (English) ones. This also covers extensions with their own resources.
    private string? GetNeutralHeader(ToolTabExtension extension)
    {
        if (!_neutralHeaders.TryGetValue(extension, out string? header))
        {
            CultureInfo culture = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
                header = extension.Header;
            }
            finally
            {
                CultureInfo.CurrentUICulture = culture;
            }

            _neutralHeaders[extension] = header;
        }

        return header;
    }
}
