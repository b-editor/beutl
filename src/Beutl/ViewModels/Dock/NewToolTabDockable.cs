using Dock.Model.Inpc.Controls;
using FluentAvalonia.UI.Controls;
using FluentIconSource = FluentIcons.Avalonia.Fluent.FluentIconSource;

namespace Beutl.ViewModels.Dock;

/// <summary>A tool the new-tab page offers.</summary>
/// <param name="IsEnabled"><see langword="false"/> for a single-instance tool that is already open.</param>
public sealed record NewToolTabItem(ToolTabExtension Extension, string Header, FAIconSource? Icon, bool IsEnabled);

/// <summary>
/// The empty tab the dock add button opens. It lists the tools and turns into the one the user
/// picks, in the same place.
/// </summary>
public sealed class NewToolTabDockable : Tool
{
    // Icon sources are cached so refreshing and filtering do not rebuild one per keystroke.
    private readonly Dictionary<ToolTabExtension, FAIconSource?> _icons = [];
    private NewToolTabItem[] _items = [];
    private IReadOnlyList<NewToolTabItem> _visibleItems = [];
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

    /// <summary>Gets the tools that match <see cref="SearchText"/>, as of the last <see cref="Refresh"/>.</summary>
    public IReadOnlyList<NewToolTabItem> VisibleItems
    {
        get => _visibleItems;
        private set => SetProperty(ref _visibleItems, value);
    }

    /// <summary>
    /// Gets or sets whether the page should take keyboard focus the next time it is shown. Only the
    /// add button sets it, so a tab restored with the layout does not steal the editor's shortcuts.
    /// </summary>
    internal bool FocusOnShow { get; set; }

    /// <summary>Re-reads the available tools and which single-instance tools are already open.</summary>
    public void Refresh()
    {
        if (Factory is not BeutlDockFactory factory)
        {
            _items = [];
        }
        else
        {
            _items = factory.EnumerateToolTabExtensions()
                .Select(extension => new NewToolTabItem(
                    extension,
                    extension.Header!,
                    GetIcon(extension),
                    extension.CanMultiple || !factory.IsToolTabOpen(extension)))
                .ToArray();
        }

        ApplyFilter();
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
        VisibleItems = query.Length == 0
            ? _items
            : _items.Where(item => Matches(item, query)).ToArray();
    }

    // The internal name is searched too, so an English name finds a tool in any UI language.
    private static bool Matches(NewToolTabItem item, string query)
    {
        return item.Header.Contains(query, StringComparison.CurrentCultureIgnoreCase)
               || item.Extension.DisplayName.Contains(query, StringComparison.CurrentCultureIgnoreCase)
               || item.Extension.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase);
    }
}
