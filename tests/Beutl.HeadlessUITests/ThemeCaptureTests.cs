using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.ElementPropertyTab.ViewModels;
using Beutl.Editor.Components.ElementPropertyTab.Views;
using Beutl.Editor.Components.FileBrowserTab.ViewModels;
using Beutl.Editor.Components.FileBrowserTab.Views;
using Beutl.Editor.Components.LibraryTab.ViewModels;
using Beutl.Editor.Components.LibraryTab.Views;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Extensibility;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.Editors;
using Beutl.Views;

namespace Beutl.HeadlessUITests;

// Pixel readback of the fully-inflated shell works locally on MoltenVK; the crash noted in
// ShellViewTests applies to SwiftShader CI only.
//
// These frames show colors AND sizes, but only the colors come from the theme. Control metrics --
// font size, row heights, slider geometry, the focused border thickness, the dropdown-glyph inset --
// come from Beutl.Controls/Styling/DesignTokens.axaml and DropDownGlyphBehavior, which are separate
// from the theme dictionaries. Read a frame as evidence about color; ControlMetricsTests is the
// authority on sizes and insets.
[TestFixture]
[Explicit("Produces PNG captures for manual design review; not a regression test.")]
public class ThemeCaptureTests
{
    private static string OutputDirectory =>
        Environment.GetEnvironmentVariable("BEUTL_THEME_CAPTURE_DIR")
        ?? Path.Combine(Path.GetTempPath(), "beutl-theme-captures");

    private static string Capture(Window window, string name)
    {
        WriteableBitmap? frame = window.CaptureRenderedFrame();
        Assert.That(frame, Is.Not.Null, "Headless frame capture returned null.");

        Directory.CreateDirectory(OutputDirectory);
        string path = Path.Combine(OutputDirectory, name);
        frame!.Save(path);
        TestContext.Out.WriteLine($"Saved capture: {path}");
        return path;
    }

    private static string NewWorkspace(string name)
    {
        string location = Path.Combine(BeutlHomeIsolation.CurrentHome!, name);
        Directory.CreateDirectory(location);
        return location;
    }

    private static async Task<EditViewModel> OpenEditorForNewScene(string name)
    {
        Project project = (await TestShell.Project.CreateProject(
            640, 480, 30, 44100, name, NewWorkspace(name)))!;
        HeadlessTestHelpers.Settle();
        Scene scene = project.Items.OfType<Scene>().First();

        TestShell.Editor.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();
        return (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
    }

    private static void AddRectangle(EditViewModel editor, TimeSpan start, int layer)
    {
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        adder.AddElement(new ElementDescription(
            Start: start,
            Length: TimeSpan.FromSeconds(2),
            Layer: layer,
            EngineObjectFactory: () => new RectShape()));
        HeadlessTestHelpers.Settle();
    }

    private static Control BuildGallery()
    {
        var comboBox = new ComboBox
        {
            Width = 220,
            ItemsSource = new[] { "Spectrum", "Waveform", "Meter" },
            SelectedIndex = 0,
        };
        var listBox = new ListBox
        {
            Width = 280,
            ItemsSource = new[] { "Templates", "PR", "PR.bep", "background.jpg" },
            SelectedIndex = 2,
        };
        var tabs = new TabControl
        {
            Items =
            {
                new TabItem { Header = "Timeline", Content = new Avalonia.Controls.TextBlock { Text = "Timeline content", Margin = new Thickness(8) } },
                new TabItem { Header = "Library" },
                new TabItem { Header = "Element Property" },
            },
        };

        return new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(24),
                Spacing = 14,
                Children =
                {
                    new Avalonia.Controls.TextBlock { Text = "Text primary", FontSize = 16 },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 10,
                        Children =
                        {
                            new Button { Content = "Standard" },
                            new Button { Content = "Accent", Classes = { "accent" } },
                            new Button { Content = "Disabled", IsEnabled = false },
                            new ToggleButton { Content = "Toggle", IsChecked = true },
                            new RepeatButton { Content = "Repeat" },
                            new DropDownButton { Content = "Menu" },
                            new HyperlinkButton { Content = "Link" },
                        },
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 10,
                        Children =
                        {
                            new TextBox { Width = 200, Text = "1920" },
                            new TextBox { Width = 200, Watermark = "Search..." },
                            comboBox,
                        },
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 10,
                        Children =
                        {
                            new TextBox { Width = 200, Text = "Disabled", IsEnabled = false },
                            new ComboBox
                            {
                                Width = 200,
                                ItemsSource = new[] { "Disabled" },
                                SelectedIndex = 0,
                                IsEnabled = false,
                            },
                        },
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 10,
                        Children =
                        {
                            new FluentAvalonia.UI.Controls.NumberBox { Value = 1920, Width = 140 },
                            new AutoCompleteBox { Text = "1920", Width = 160 },
                            new FluentAvalonia.UI.Controls.ColorPickerButton(),
                            new Button
                            {
                                Name = "GalleryMenuButton",
                                Content = "Context menu",
                                Flyout = new FluentAvalonia.UI.Controls.FAMenuFlyout
                                {
                                    Items =
                                    {
                                        new FluentAvalonia.UI.Controls.MenuFlyoutItem { Text = "Cut" },
                                        new FluentAvalonia.UI.Controls.MenuFlyoutItem { Text = "Copy" },
                                        new FluentAvalonia.UI.Controls.MenuFlyoutSeparator(),
                                        new FluentAvalonia.UI.Controls.ToggleMenuFlyoutItem { Text = "Snap to grid", IsChecked = true },
                                    },
                                },
                            },
                        },
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 16,
                        Children =
                        {
                            new CheckBox { Content = "Checked", IsChecked = true },
                            new CheckBox { Content = "Unchecked" },
                            new ToggleSwitch { IsChecked = true },
                            new ToggleSwitch { IsChecked = false },
                        },
                    },
                    new Slider { Width = 240, Minimum = 0, Maximum = 100, Value = 60, HorizontalAlignment = HorizontalAlignment.Left },
                    new ProgressBar { Width = 240, Minimum = 0, Maximum = 100, Value = 45, HorizontalAlignment = HorizontalAlignment.Left },
                    tabs,
                    listBox,
                    new Expander { Header = "Expander", IsExpanded = true, Content = new Avalonia.Controls.TextBlock { Text = "Nested content", Margin = new Thickness(8) } },
                    new Border
                    {
                        Classes = { "CardStyle" },
                        Padding = new Thickness(12),
                        Child = new Avalonia.Controls.TextBlock { Text = "Border.CardStyle" },
                    },
                },
            },
        };
    }

    [AvaloniaTest]
    public async Task Capture_editor_shell_dark()
    {
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("themecapture");
        for (int layer = 0; layer < 3; layer++)
        {
            AddRectangle(editor, TimeSpan.FromSeconds(layer * 0.5), layer);
        }

        var window = new Window
        {
            Content = new EditView { DataContext = editor },
            Width = 1500,
            Height = 960
        };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(5);
            Capture(window, "editor-shell-themed.png");
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    private static RectShape CreateDecoratedRect()
    {
        var rect = new RectShape();
        rect.Fill.CurrentValue = new SolidColorBrush(0xFF3B82F6);

        var pen = new Pen();
        pen.Brush.CurrentValue = new SolidColorBrush(0xFFE9EBF1);
        pen.Thickness.CurrentValue = 4;
        rect.Pen.CurrentValue = pen;

        var blur = new Blur();
        blur.Sigma.CurrentValue = new Graphics.Size(10, 10);
        ((FilterEffectGroup)rect.FilterEffect.CurrentValue!).Children.Add(blur);
        return rect;
    }

    private static void ExpandFilterEffectEditors(ElementPropertyTabViewModel tab)
    {
        static IEnumerable<IPropertyEditorContext> Flatten(IEnumerable<IPropertyEditorContext?> contexts)
        {
            foreach (IPropertyEditorContext? context in contexts)
            {
                if (context == null) continue;
                yield return context;
                if (context is PropertyEditorGroupContext group)
                {
                    foreach (IPropertyEditorContext nested in Flatten(group.Properties))
                    {
                        yield return nested;
                    }
                }
            }
        }

        foreach (EngineObjectPropertyViewModel item in tab.Items)
        {
            foreach (FilterEffectEditorViewModel editor in Flatten(item.Properties).OfType<FilterEffectEditorViewModel>())
            {
                editor.IsExpanded.Value = true;
            }
        }
    }

    [AvaloniaTest]
    public async Task Capture_element_properties_dark()
    {
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("inspectorcapture");

        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        adder.AddElement(new ElementDescription(
            Start: TimeSpan.Zero,
            Length: TimeSpan.FromSeconds(2),
            Layer: 0,
            EngineObjectFactory: CreateDecoratedRect));
        HeadlessTestHelpers.Settle();

        Element element = editor.Scene.Children.First();
        var selection = (IEditorSelection)editor.GetService(typeof(IEditorSelection))!;
        selection.SelectedObject.Value = element;
        HeadlessTestHelpers.Settle();

        ElementPropertyTabViewModel? shellTab = editor.FindToolTab<ElementPropertyTabViewModel>();
        Assert.That(shellTab, Is.Not.Null, "Element Property tab missing from the default layout.");
        ExpandFilterEffectEditors(shellTab!);
        HeadlessTestHelpers.Settle();

        var shellWindow = new Window
        {
            Content = new EditView { DataContext = editor },
            Width = 1600,
            Height = 1000
        };
        try
        {
            shellWindow.Show();
            HeadlessTestHelpers.Render(8);
            Capture(shellWindow, "inspector-shell-dark.png");
        }
        finally
        {
            shellWindow.Close();
            HeadlessTestHelpers.Settle();
        }

        var closeUpVm = new ElementPropertyTabViewModel(editor);
        ExpandFilterEffectEditors(closeUpVm);
        var closeUpWindow = new Window
        {
            Content = new ElementPropertyTabView { DataContext = closeUpVm },
            Width = 380,
            Height = 900
        };
        try
        {
            closeUpWindow.Show();
            HeadlessTestHelpers.Render(8);
            Capture(closeUpWindow, "inspector-closeup-dark.png");
        }
        finally
        {
            closeUpWindow.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    private static void ExpandAndSelectLibraryItems(Window window, LibraryItemViewModel group, LibraryItemViewModel selected)
    {
        TreeView tree = window.GetVisualDescendants().OfType<TreeView>()
            .First(t => t.Name == "LibraryTree");
        tree.SelectedItem = selected;
        if (tree.ContainerFromItem(group) is TreeViewItem container)
        {
            container.IsExpanded = true;
        }
    }

    [AvaloniaTest]
    public async Task Capture_library_dark()
    {
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("librarycapture");

        LibraryTabViewModel? library = editor.FindToolTab<LibraryTabViewModel>();
        Assert.That(library, Is.Not.Null, "Library tab missing from the default layout.");
        LibraryItemViewModel group = library!.LibraryItems.First(i => i.Children.Count > 0);
        LibraryItemViewModel selected = library.LibraryItems.FirstOrDefault(i => i.DisplayName == "Rectangle")
            ?? library.LibraryItems.First(i => i.Children.Count == 0);

        var shellWindow = new Window
        {
            Content = new EditView { DataContext = editor },
            Width = 1600,
            Height = 1000
        };
        try
        {
            shellWindow.Show();
            HeadlessTestHelpers.Render(5);
            ExpandAndSelectLibraryItems(shellWindow, group, selected);
            HeadlessTestHelpers.Render(5);
            Capture(shellWindow, "library-shell-dark.png");
        }
        finally
        {
            shellWindow.Close();
            HeadlessTestHelpers.Settle();
        }

        var closeUpWindow = new Window
        {
            Content = new LibraryTabView { DataContext = library },
            Width = 380,
            Height = 900
        };
        try
        {
            closeUpWindow.Show();
            HeadlessTestHelpers.Render(5);
            ExpandAndSelectLibraryItems(closeUpWindow, group, selected);
            HeadlessTestHelpers.Render(5);
            Capture(closeUpWindow, "library-closeup-dark.png");
        }
        finally
        {
            closeUpWindow.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task Capture_file_browser_dark()
    {
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("filebrowsercapture");

        FileBrowserTabViewModel? browser = editor.FindToolTab<FileBrowserTabViewModel>();
        Assert.That(browser, Is.Not.Null, "File Browser tab missing from the default layout.");
        editor.OpenToolTab(browser!);
        HeadlessTestHelpers.Settle();

        Project project = editor.Scene.FindRequiredHierarchicalParent<Project>();
        string projectDirectory = Path.GetDirectoryName(project.Uri!.LocalPath)!;
        browser!.ViewMode.Value = FileBrowserViewMode.Tree;
        browser.RootPath.Value = projectDirectory;
        HeadlessTestHelpers.Settle();
        Assert.That(browser.TreeRootItems, Is.Not.Empty, "Project directory produced no file tree nodes.");

        FileSystemItemViewModel? directory = browser.TreeRootItems.FirstOrDefault(i => i.IsDirectory);
        if (directory != null)
        {
            directory.IsExpanded.Value = true;
            HeadlessTestHelpers.Settle();
        }

        FileSystemItemViewModel? file = directory?.Children?.FirstOrDefault(c => !c.IsDirectory)
            ?? browser.TreeRootItems.FirstOrDefault(i => !i.IsDirectory);

        void SelectFileInTree(Window window)
        {
            if (file == null) return;
            TreeView tree = window.GetVisualDescendants().OfType<TreeView>()
                .First(t => ReferenceEquals(t.ItemsSource, browser.TreeRootItems));
            tree.SelectedItem = file;
        }

        var shellWindow = new Window
        {
            Content = new EditView { DataContext = editor },
            Width = 1600,
            Height = 1000
        };
        try
        {
            shellWindow.Show();
            HeadlessTestHelpers.Render(8);
            SelectFileInTree(shellWindow);
            HeadlessTestHelpers.Render(3);
            Capture(shellWindow, "filebrowser-shell-dark.png");
        }
        finally
        {
            shellWindow.Close();
            HeadlessTestHelpers.Settle();
        }

        var closeUpWindow = new Window
        {
            Content = new FileBrowserTabView { DataContext = browser },
            Width = 380,
            Height = 900
        };
        try
        {
            closeUpWindow.Show();
            HeadlessTestHelpers.Render(8);
            SelectFileInTree(closeUpWindow);
            HeadlessTestHelpers.Render(3);
            Capture(closeUpWindow, "filebrowser-closeup-dark.png");
        }
        finally
        {
            closeUpWindow.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public void Capture_control_gallery_dark()
    {
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        var window = new Window { Content = BuildGallery(), Width = 860, Height = 940 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);

            // Open the flyout so the capture also shows popup styling.
            var menuButton = window.GetVisualDescendants().OfType<Button>()
                .First(b => b.Name == "GalleryMenuButton");
            menuButton.Flyout!.ShowAt(menuButton);
            HeadlessTestHelpers.Render(3);

            Capture(window, "gallery-dark.png");
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public void Capture_combobox_detail_dark()
    {
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        var window = new Window
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(12),
                Children =
                {
                    new TextBox { Text = "1920", Width = 110, VerticalAlignment = VerticalAlignment.Top },
                    new ComboBox { ItemsSource = new[] { "Spectrum" }, SelectedIndex = 0, Width = 120, VerticalAlignment = VerticalAlignment.Top },
                    new ComboBox { ItemsSource = new[] { "Spectrum" }, PlaceholderText = "Select...", Width = 120, VerticalAlignment = VerticalAlignment.Top },
                },
            },
            Width = 400,
            Height = 120,
        };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            Capture(window, "combobox-detail-dark.png");
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public void Capture_flyout_selected_tab_dark()
    {
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;

        var presenter = new BrushEditorFlyoutPresenter
        {
            Width = 240,
            Content = new SimpleColorPicker(),
        };
        var window = new Window { Content = presenter, Width = 320, Height = 520 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);

            // Mark the first brush-type tab active so the capture shows the selected-tab treatment.
            ToggleButton solidTab = window.GetVisualDescendants().OfType<ToggleButton>()
                .First(b => b.Name == "SolidBrushTabButton");
            solidTab.IsChecked = true;
            HeadlessTestHelpers.Render(3);

            Capture(window, "flyout-tabs-dark.png");
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public void Capture_textbox_border_dark()
    {
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;

        var restBox = new TextBox { Text = "Rest", Width = 200 };
        var focusedBox = new TextBox { Text = "Focused", Width = 200 };
        var window = new Window
        {
            Content = new StackPanel
            {
                Margin = new Thickness(24),
                Spacing = 16,
                Children = { restBox, focusedBox },
            },
            Width = 280,
            Height = 160,
        };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            focusedBox.Focus();
            HeadlessTestHelpers.Render(3);
            Capture(window, "textbox-border-dark.png");
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public void Capture_control_gallery_light()
    {
        Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        var window = new Window { Content = BuildGallery(), Width = 860, Height = 940 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            Capture(window, "gallery-light.png");
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
            Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        }
    }
}
