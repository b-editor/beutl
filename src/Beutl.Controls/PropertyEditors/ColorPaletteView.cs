using System.Collections.Immutable;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Beutl.Language;
using FluentAvalonia.Core;
using FluentAvalonia.UI.Controls;
using FluentIcons.Avalonia.Fluent;
using FluentIcons.Common;

namespace Beutl.Controls.PropertyEditors;

// The palette tab of the color pickers: a button that makes a new palette, then the recent colors, the open
// project's palettes, the user's palettes and the default palette, each under its name. A click on a color raises ColorSelected. The palettes of the project
// and the user take the current color with their + button, and their menus name, order, move and delete them.
public sealed class ColorPaletteView : TemplatedControl
{
    public static readonly StyledProperty<Color> CurrentColorProperty =
        AvaloniaProperty.Register<ColorPaletteView, Color>(nameof(CurrentColor));

    public static readonly StyledProperty<int> ColumnsProperty =
        AvaloniaProperty.Register<ColorPaletteView, int>(nameof(Columns), 10, validate: v => v > 0);

    private readonly List<ColorPaletteItem> _items = [];
    private ColorPaletteService? _service;
    private ColorPaletteService? _subscribedService;
    private StackPanel? _sections;

    public ColorPaletteView()
    {
        AddHandler(ColorPaletteItem.ClickEvent, OnItemClick);
        AddHandler(ContextRequestedEvent, OnContextRequested);
    }

    public event TypedEventHandler<ColorPaletteView, Color>? ColorSelected;

    // The color being edited: the colors equal to it are ringed, and + adds it.
    public Color CurrentColor
    {
        get => GetValue(CurrentColorProperty);
        set => SetValue(CurrentColorProperty, value);
    }

    public int Columns
    {
        get => GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    // The shared service unless a test gives the view one of its own.
    internal ColorPaletteService Service
    {
        get => _service ?? ColorPaletteService.Instance;
        set
        {
            _service = value;
            if (_subscribedService != null)
            {
                Subscribe();
            }

            Rebuild();
        }
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _sections = e.NameScope.Find<StackPanel>("PART_Sections");
        Rebuild();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Subscribe();
        Rebuild();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Unsubscribe();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CurrentColorProperty)
        {
            UpdateSelection();
        }
        else if (change.Property == ColumnsProperty)
        {
            Rebuild();
        }
    }

    private void Subscribe()
    {
        Unsubscribe();
        _subscribedService = Service;
        _subscribedService.Changed += OnServiceChanged;
    }

    private void Unsubscribe()
    {
        if (_subscribedService != null)
        {
            _subscribedService.Changed -= OnServiceChanged;
            _subscribedService = null;
        }
    }

    private void OnServiceChanged(object? sender, EventArgs e)
    {
        Rebuild();
    }

    private void Rebuild()
    {
        if (_sections == null) return;

        _sections.Children.Clear();
        _items.Clear();
        ColorPaletteService service = Service;

        // First, so that it stays in reach however many palettes there are.
        _sections.Children.Add(CreateNewPaletteButton());

        if (!service.RecentColors.IsEmpty)
        {
            AddHeader(Strings.ColorPalette_RecentColors, new PaletteHeader(SwatchKind.Recent, default, -1));
            AddSwatches(service.RecentColors
                .Select((c, i) => new Swatch(SwatchKind.Recent, default, -1, i, new PaletteColor(c.ToUInt32()))));
        }

        foreach (PaletteScope scope in (ReadOnlySpan<PaletteScope>)[PaletteScope.Project, PaletteScope.User])
        {
            ImmutableArray<ColorPalette> palettes = service.GetPalettes(scope);
            for (int i = 0; i < palettes.Length; i++)
            {
                AddEditablePalette(scope, i, palettes[i]);
            }
        }

        ColorPalette defaultPalette = DefaultColorPalette.Palette;
        AddHeader(defaultPalette.Name, new PaletteHeader(SwatchKind.Default, default, -1));
        AddSwatches(defaultPalette.Colors.Select((c, i) => new Swatch(SwatchKind.Default, default, -1, i, c)));

        UpdateSelection();
    }

    private void AddEditablePalette(PaletteScope scope, int index, ColorPalette palette)
    {
        var header = new PaletteHeader(SwatchKind.Editable, scope, index);
        Button add = CreateHeaderButton(Icon.Add, Strings.ColorPalette_AddCurrentColor);
        add.Click += (_, _) => Service.EditPalette(scope, index, p => p.AddColor(new PaletteColor(CurrentColor.ToUInt32())));
        Button actions = CreateHeaderButton(Icon.MoreHorizontal, Strings.ColorPalette_PaletteActions);
        actions.Click += (_, _) => CreatePaletteMenu(header).ShowAt(actions);
        AddHeader(palette.Name, header, scope == PaletteScope.Project, add, actions);

        if (palette.Colors.IsEmpty)
        {
            _sections!.Children.Add(new TextBlock
            {
                Classes = { "palette-hint" },
                Text = Strings.ColorPalette_EmptyHint,
                TextWrapping = TextWrapping.Wrap
            });
        }
        else
        {
            AddSwatches(palette.Colors.Select((c, i) => new Swatch(SwatchKind.Editable, scope, index, i, c)));
        }
    }

    private void AddHeader(string name, PaletteHeader header, bool isProject = false, params Control[] buttons)
    {
        var grid = new Grid
        {
            Classes = { "palette-header" },
            Tag = header,
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto")
        };

        if (isProject)
        {
            var icon = new FluentIcon { Classes = { "palette-project-icon" }, Icon = Icon.DocumentFolder };
            ToolTip.SetTip(icon, Strings.ColorPalette_SavedInProject);
            grid.Children.Add(icon);
        }

        var text = new TextBlock { Classes = { "palette-name" }, Text = name };
        Grid.SetColumn(text, 1);
        ToolTip.SetTip(text, name);
        grid.Children.Add(text);

        for (int i = 0; i < buttons.Length; i++)
        {
            Grid.SetColumn(buttons[i], 2 + i);
            grid.Children.Add(buttons[i]);
        }

        _sections!.Children.Add(grid);
    }

    private static Button CreateHeaderButton(Icon icon, string tip)
    {
        var button = new Button
        {
            Classes = { "palette-header-button" },
            Content = new FluentIcon { Icon = icon }
        };
        ToolTip.SetTip(button, tip);
        return button;
    }

    private Button CreateNewPaletteButton()
    {
        var button = new Button
        {
            Name = "NewPaletteButton",
            Classes = { "palette-new-button" },
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new FluentIcon { Icon = Icon.Add },
                    new TextBlock { Text = Strings.ColorPalette_NewPalette }
                }
            }
        };
        button.Click += (_, _) => AskName(button, Strings.ColorPalette_NewPalette, Service.CreatePaletteName(),
            name => Service.AddPalette(PaletteScope.User, new ColorPalette(name, [])));
        return button;
    }

    private void AddSwatches(IEnumerable<Swatch> swatches)
    {
        var panel = new ColorSwatchPanel { Columns = Columns };
        foreach (Swatch swatch in swatches)
        {
            Color color = Color.FromUInt32(swatch.Color.Argb);
            // The item sets a minimum size of its own, which would overflow a narrow cell.
            var item = new ColorPaletteItem
            {
                Classes = { "palette-swatch" },
                Color = color,
                Tag = swatch,
                MinWidth = 0,
                MinHeight = 0
            };
            ToolTip.SetTip(item, FormatTip(swatch.Color));
            panel.Children.Add(item);
            _items.Add(item);
        }

        _sections!.Children.Add(panel);
    }

    private void UpdateSelection()
    {
        Color current = CurrentColor;
        foreach (ColorPaletteItem item in _items)
        {
            item.IsSelected = item.Color == current;
        }
    }

    private void OnItemClick(object? sender, RoutedEventArgs e)
    {
        if (e.Source is ColorPaletteItem { Tag: Swatch } item)
        {
            e.Handled = true;
            ColorSelected?.Invoke(this, item.Color);
        }
    }

    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        for (Control? element = e.Source as Control; element != null && element != this; element = element.Parent as Control)
        {
            FAMenuFlyout? menu = element.Tag switch
            {
                Swatch swatch => CreateSwatchMenu(element, swatch),
                PaletteHeader header => CreatePaletteMenu(header),
                _ => null
            };

            if (menu != null)
            {
                e.Handled = true;
                if (menu.Items.Count > 0)
                {
                    menu.ShowAt(element, showAtPointer: true);
                }

                return;
            }
        }
    }

    private FAMenuFlyout CreatePaletteMenu(PaletteHeader header)
    {
        var menu = new FAMenuFlyout();
        ColorPaletteService service = Service;
        if (header.Kind == SwatchKind.Recent)
        {
            menu.Items.Add(CreateItem(Strings.ColorPalette_ClearRecent, Icon.Eraser, service.ClearRecentColors));
            return menu;
        }

        if (header.Kind != SwatchKind.Editable)
        {
            return menu;
        }

        (PaletteScope scope, int index) = (header.Scope, header.Palette);
        ImmutableArray<ColorPalette> palettes = service.GetPalettes(scope);
        menu.Items.Add(CreateItem(Strings.Rename, Icon.Rename, () =>
            AskName((Control?)FindHeader(header) ?? this, Strings.Rename, palettes[index].Name,
                name => service.EditPalette(scope, index, p => p.WithName(name)))));
        menu.Items.Add(CreateItem(Strings.ColorPalette_MoveUp, Icon.ArrowUp,
            () => service.MovePalette(scope, index, index - 1), isEnabled: index > 0));
        menu.Items.Add(CreateItem(Strings.ColorPalette_MoveDown, Icon.ArrowDown,
            () => service.MovePalette(scope, index, index + 1), isEnabled: index < palettes.Length - 1));

        if (scope == PaletteScope.User && service.IsAvailable(PaletteScope.Project))
        {
            menu.Items.Add(CreateItem(Strings.ColorPalette_SaveInProject, Icon.DocumentFolder,
                () => service.MovePaletteToScope(scope, index, PaletteScope.Project)));
        }
        else if (scope == PaletteScope.Project)
        {
            menu.Items.Add(CreateItem(Strings.ColorPalette_UseInAllProjects, Icon.Globe,
                () => service.MovePaletteToScope(scope, index, PaletteScope.User)));
        }

        menu.Items.Add(new FAMenuFlyoutSeparator());
        menu.Items.Add(CreateItem(Strings.Delete, Icon.Delete, () => service.RemovePalette(scope, index)));
        return menu;
    }

    private FAMenuFlyout CreateSwatchMenu(Control target, Swatch swatch)
    {
        var menu = new FAMenuFlyout();
        ColorPaletteService service = Service;
        if (swatch.Kind == SwatchKind.Editable)
        {
            (PaletteScope scope, int palette, int index) = (swatch.Scope, swatch.Palette, swatch.Index);
            menu.Items.Add(CreateItem(Strings.Rename, Icon.Rename, () =>
                AskName(target, Strings.Rename, swatch.Color.Name ?? string.Empty,
                    name => service.EditPalette(scope, palette,
                        p => p.SetColor(index, swatch.Color with { Name = name.Length > 0 ? name : null })),
                    allowEmpty: true)));

            uint current = CurrentColor.ToUInt32();
            menu.Items.Add(CreateItem(Strings.ColorPalette_ReplaceWithCurrentColor, Icon.ColorFill,
                () => service.EditPalette(scope, palette, p => p.SetColor(index, swatch.Color with { Argb = current })),
                isEnabled: current != swatch.Color.Argb));

            var moveTo = new FAMenuFlyoutSubItem { Text = Strings.MoveTo, IconSource = new FluentIconSource { Icon = Icon.ArrowMove } };
            foreach ((PaletteScope Scope, int Index, ColorPalette Palette) destination in GetEditablePalettes())
            {
                if (destination.Scope != scope || destination.Index != palette)
                {
                    moveTo.Items.Add(CreateDestinationItem(destination,
                        () => service.MoveColor(scope, palette, index, destination.Scope, destination.Index)));
                }
            }

            if (moveTo.Items.Count > 0)
            {
                menu.Items.Add(moveTo);
            }

            menu.Items.Add(new FAMenuFlyoutSeparator());
            menu.Items.Add(CreateItem(Strings.Delete, Icon.Delete,
                () => service.EditPalette(scope, palette, p => p.RemoveColor(index))));
            return menu;
        }

        // A color of the recent colors or of the default palette is copied into a palette of the user's.
        PaletteColor color = swatch.Color;
        var destinations = GetEditablePalettes().ToArray();
        void AddToNewPalette() => AskName(target, Strings.ColorPalette_NewPalette, service.CreatePaletteName(),
            name => service.AddPalette(PaletteScope.User, new ColorPalette(name, [color])));

        if (destinations.Length == 0)
        {
            menu.Items.Add(CreateItem(Strings.ColorPalette_AddToNewPalette, Icon.Add, AddToNewPalette));
        }
        else
        {
            var addTo = new FAMenuFlyoutSubItem { Text = Strings.ColorPalette_AddToPalette, IconSource = new FluentIconSource { Icon = Icon.Add } };
            foreach ((PaletteScope Scope, int Index, ColorPalette Palette) destination in destinations)
            {
                addTo.Items.Add(CreateDestinationItem(destination,
                    () => service.EditPalette(destination.Scope, destination.Index, p => p.AddColor(color))));
            }

            addTo.Items.Add(new FAMenuFlyoutSeparator());
            addTo.Items.Add(CreateItem(Strings.ColorPalette_NewPalette, Icon.Add, AddToNewPalette));
            menu.Items.Add(addTo);
        }

        if (swatch.Kind == SwatchKind.Recent)
        {
            Color recent = Color.FromUInt32(color.Argb);
            menu.Items.Add(CreateItem(Strings.ColorPalette_RemoveFromRecent, Icon.Dismiss,
                () => service.RemoveRecentColor(recent)));
        }

        return menu;
    }

    private IEnumerable<(PaletteScope Scope, int Index, ColorPalette Palette)> GetEditablePalettes()
    {
        foreach (PaletteScope scope in (PaletteScope[])[PaletteScope.Project, PaletteScope.User])
        {
            ImmutableArray<ColorPalette> palettes = Service.GetPalettes(scope);
            for (int i = 0; i < palettes.Length; i++)
            {
                yield return (scope, i, palettes[i]);
            }
        }
    }

    private Grid? FindHeader(PaletteHeader header)
    {
        return _sections?.Children.OfType<Grid>().FirstOrDefault(g => Equals(g.Tag, header));
    }

    // Asks for a name, then gives the name to apply, trimmed. An empty name keeps the old one unless it may be empty.
    private static void AskName(Control target, string header, string text, Action<string> apply, bool allowEmpty = false)
    {
        var flyout = new PaletteNameFlyout(header) { Text = text, Placement = PlacementMode.BottomEdgeAlignedLeft };
        flyout.Confirmed += (_, value) =>
        {
            string name = value?.Trim() ?? string.Empty;
            if (name.Length > 0 || allowEmpty)
            {
                apply(name);
            }
        };
        flyout.ShowAt(target);
    }

    private static FAMenuFlyoutItem CreateItem(string text, Icon icon, Action action, bool isEnabled = true)
    {
        var item = new FAMenuFlyoutItem
        {
            Text = text,
            IconSource = new FluentIconSource { Icon = icon },
            IsEnabled = isEnabled
        };
        item.Click += (_, _) => action();
        return item;
    }

    private static FAMenuFlyoutItem CreateDestinationItem(
        (PaletteScope Scope, int Index, ColorPalette Palette) destination, Action action)
    {
        var item = new FAMenuFlyoutItem { Text = destination.Palette.Name };
        if (destination.Scope == PaletteScope.Project)
        {
            item.IconSource = new FluentIconSource { Icon = Icon.DocumentFolder };
        }

        item.Click += (_, _) => action();
        return item;
    }

    // The name, if any, over the color as #RRGGBB, or #AARRGGBB when it is not opaque.
    private static string FormatTip(PaletteColor color)
    {
        string hex = color.Argb >> 24 == 0xFF
            ? "#" + (color.Argb & 0xFFFFFF).ToString("X6", CultureInfo.InvariantCulture)
            : ColorPalette.FormatArgb(color.Argb);
        return string.IsNullOrEmpty(color.Name) ? hex : $"{color.Name}\n{hex}";
    }

    private enum SwatchKind
    {
        Recent,
        Editable,
        Default
    }

    // A color as the view shows it: which list it is in and where. Scope and Palette apply to Editable only.
    private sealed record Swatch(SwatchKind Kind, PaletteScope Scope, int Palette, int Index, PaletteColor Color);

    private sealed record PaletteHeader(SwatchKind Kind, PaletteScope Scope, int Palette);
}
