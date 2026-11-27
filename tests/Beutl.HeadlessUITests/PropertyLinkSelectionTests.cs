using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.ElementPropertyTab.ViewModels;
using Beutl.Editor.Components.ElementPropertyTab.Views;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.ObjectPropertyTab.ViewModels;
using Beutl.Editor.Components.ObjectPropertyTab.Views;
using Beutl.Editor.Components.PropertyEditors;
using Beutl.Editor.Components.PropertyEditors.Views;
using Beutl.Editor.PropertyEditors;
using Beutl.Editor.Services;
using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using Point = Avalonia.Point;

namespace Beutl.HeadlessUITests;

// Linking a property to another one by clicking it, as the property menu offers.
[TestFixture]
[NonParallelizable]
public sealed class PropertyLinkSelectionTests
{
    [AvaloniaTest]
    public async Task A_float_property_links_to_another_float_property_and_follows_it()
    {
        using LinkHost host = await LinkHost.OpenAsync();
        var (window, editor, _, source, target, _, _) = host;
        int before = editor.HistoryManager.UndoCount;

        await StartLinkAsync(window, source.Opacity);
        PropertyEditor targetRow = await ScrollToRowAsync(window, target.Opacity);
        PropertyLinkSelectionOverlay overlay = OverlayOf(targetRow);
        Assert.Multiple(() =>
        {
            Assert.That(overlay.IsEffectivelyVisible, Is.True);
            Assert.That(overlay.IsCandidate, Is.True);
        });

        await HoverAsync(window, overlay);
        Assert.That(overlay.IsHighlighted, Is.True);
        Capture(window, "01-selecting-a-target");

        HeadlessTestHelpers.Click(targetRow);
        HeadlessTestHelpers.Settle();
        Capture(window, "02-linked");

        string expected = $"GetProperty<float>(\"{{{target.Id.Value}}}.Opacity\")";
        Assert.Multiple(() =>
        {
            Assert.That(Selection(editor).IsSelectingLinkTarget.Value, Is.False);
            Assert.That(source.Opacity.Expression?.ExpressionString, Is.EqualTo(expected));
            Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(before + 1));
        });

        target.Opacity.Value = 37;
        Assert.That(source.Opacity.Evaluate(new EvaluationContext(MediaTime.Zero)), Is.EqualTo(37f));
    }

    [AvaloniaTest]
    public async Task A_row_of_another_type_is_not_highlighted_and_ignores_the_click()
    {
        using LinkHost host = await LinkHost.OpenAsync();
        var (window, editor, _, source, target, _, _) = host;
        int before = editor.HistoryManager.UndoCount;

        await StartLinkAsync(window, source.Opacity);
        PropertyEditor otherRow = await ScrollToRowAsync(window, target.BlendMode);
        PropertyLinkSelectionOverlay overlay = OverlayOf(otherRow);
        Assert.That(overlay.IsCandidate, Is.False);

        await HoverAsync(window, overlay);
        Assert.Multiple(() =>
        {
            Assert.That(overlay.IsHighlighted, Is.False);
            Assert.That(overlay.Cursor, Is.SameAs(Cursors.Arrow));
        });

        HeadlessTestHelpers.Click(otherRow);
        HeadlessTestHelpers.Settle();
        Assert.Multiple(() =>
        {
            Assert.That(Selection(editor).IsSelectingLinkTarget.Value, Is.True, "A mismatched row leaves the picking on.");
            Assert.That(source.Opacity.Expression, Is.Null);
            Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(before));
        });

        PressEscape(window);
        Assert.That(Selection(editor).IsSelectingLinkTarget.Value, Is.False);
    }

    [AvaloniaTest]
    public async Task Another_editor_of_the_same_property_is_not_a_target()
    {
        using LinkHost host = await LinkHost.OpenAsync();
        var (window, editor, _, source, _, _, elementView) = host;
        int before = editor.HistoryManager.UndoCount;

        using var objectTab = new ObjectPropertyTabViewModel(editor);
        objectTab.NavigateCore(source, back: false, provider: null);
        HeadlessTestHelpers.Settle();
        var objectView = new ObjectPropertyTabView { DataContext = objectTab };

        window.Content = null;
        var panels = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
        Grid.SetColumn(elementView, 0);
        Grid.SetColumn(objectView, 1);
        panels.Children.Add(elementView);
        panels.Children.Add(objectView);
        window.Content = panels;
        window.Width = 1440;
        HeadlessTestHelpers.Render(3);

        await StartLinkAsync(elementView, source.Opacity);
        PropertyEditor duplicateRow = await ScrollToRowAsync(objectView, source.Opacity);
        PropertyLinkSelectionOverlay overlay = OverlayOf(duplicateRow);
        Assert.That(overlay.IsCandidate, Is.False, "The same property in another panel is the property itself.");

        await HoverAsync(window, overlay);
        Assert.That(overlay.IsHighlighted, Is.False);

        HeadlessTestHelpers.Click(duplicateRow);
        HeadlessTestHelpers.Settle();
        Assert.Multiple(() =>
        {
            Assert.That(Selection(editor).IsSelectingLinkTarget.Value, Is.True);
            Assert.That(source.Opacity.Expression, Is.Null);
            Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(before));
        });

        PressEscape(window);
    }

    [AvaloniaTest]
    public async Task A_detached_property_tab_stops_following_the_picking()
    {
        using LinkHost host = await LinkHost.OpenAsync();
        var (window, editor, _, source, _, _, view) = host;
        PropertyEditor row = await ScrollToRowAsync(view, source.Opacity);
        var sourceEditor = (PropertyEditorViewModel)row.DataContext!;
        Border prompt = view.FindControl<Border>("linkSelectionPrompt")!;
        IPropertyLinkSelection selection = Selection(editor);

        // The tab keeps its editors while it is away, so the picking can name a property of it.
        window.Content = null;
        HeadlessTestHelpers.Render(3);
        selection.BeginLinkSelection(sourceEditor);
        HeadlessTestHelpers.Render();
        Assert.That(prompt.IsVisible, Is.False, "A detached view no longer follows the service.");
        selection.CancelLinkSelection();

        window.Content = view;
        HeadlessTestHelpers.Render(3);
        selection.BeginLinkSelection(sourceEditor);
        HeadlessTestHelpers.Render();
        Assert.That(prompt.IsVisible, Is.True, "Showing the tab again subscribes it again.");
        selection.CancelLinkSelection();
    }

    [AvaloniaTest]
    public async Task Clicking_outside_the_property_rows_cancels_the_picking()
    {
        using LinkHost host = await LinkHost.OpenAsync();
        var (window, editor, _, source, _, _, _) = host;
        int before = editor.HistoryManager.UndoCount;

        await StartLinkAsync(window, source.Opacity);
        EngineObjectPropertyView objectView = window.GetVisualDescendants().OfType<EngineObjectPropertyView>()
            .First(view => view.DataContext is EngineObjectPropertyViewModel model
                && ReferenceEquals(model.Model, source));
        Grid header = objectView.FindControl<Grid>("headerPanel")!;
        header.BringIntoView();
        HeadlessTestHelpers.Render(3);
        Assert.That(Selection(editor).IsSelectingLinkTarget.Value, Is.True);

        HeadlessTestHelpers.Click(header);
        HeadlessTestHelpers.Settle();
        Assert.Multiple(() =>
        {
            Assert.That(Selection(editor).IsSelectingLinkTarget.Value, Is.False);
            Assert.That(source.Opacity.Expression, Is.Null);
            Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(before));
        });
    }

    [AvaloniaTest]
    public async Task Choosing_the_menu_item_again_cancels_the_picking()
    {
        using LinkHost host = await LinkHost.OpenAsync();
        var (window, editor, _, source, _, _, _) = host;
        int before = editor.HistoryManager.UndoCount;

        await StartLinkAsync(window, source.Opacity);
        Assert.That(Selection(editor).IsSelectingLinkTarget.Value, Is.True);

        await StartLinkAsync(window, source.Opacity);
        Assert.Multiple(() =>
        {
            Assert.That(Selection(editor).IsSelectingLinkTarget.Value, Is.False);
            Assert.That(source.Opacity.Expression, Is.Null);
            Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(before));
        });
    }

    [AvaloniaTest]
    public async Task Escape_cancels_without_setting_an_expression()
    {
        using LinkHost host = await LinkHost.OpenAsync();
        var (window, editor, _, source, _, _, view) = host;
        int before = editor.HistoryManager.UndoCount;

        await StartLinkAsync(window, source.Opacity);
        Border prompt = view.FindControl<Border>("linkSelectionPrompt")!;
        Assert.Multiple(() =>
        {
            Assert.That(Selection(editor).IsSelectingLinkTarget.Value, Is.True);
            Assert.That(prompt.IsEffectivelyVisible, Is.True);
            Assert.That(OverlayOf(RowOf(window, source.Opacity)).IsEffectivelyVisible, Is.True);
            Assert.That(OverlayOf(RowOf(window, source.Opacity)).IsCandidate, Is.False,
                "The property itself is not a target.");
        });

        PressEscape(window);
        Assert.Multiple(() =>
        {
            Assert.That(Selection(editor).IsSelectingLinkTarget.Value, Is.False);
            Assert.That(prompt.IsEffectivelyVisible, Is.False);
            Assert.That(source.Opacity.Expression, Is.Null);
            Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(before));
        });
    }

    [AvaloniaTest]
    public async Task Undo_removes_the_link_and_redo_restores_it()
    {
        using LinkHost host = await LinkHost.OpenAsync();
        var (window, editor, _, source, target, _, _) = host;
        int before = editor.HistoryManager.UndoCount;
        string expected = $"GetProperty<float>(\"{{{target.Id.Value}}}.Opacity\")";

        await LinkAsync(window, source.Opacity, target.Opacity);
        Assert.Multiple(() =>
        {
            Assert.That(source.Opacity.Expression?.ExpressionString, Is.EqualTo(expected));
            Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(before + 1));
        });

        editor.HistoryManager.Undo();
        HeadlessTestHelpers.Settle();
        Assert.That(source.Opacity.Expression, Is.Null);

        editor.HistoryManager.Redo();
        HeadlessTestHelpers.Settle();
        Assert.That(source.Opacity.Expression?.ExpressionString, Is.EqualTo(expected));
    }

    [AvaloniaTest]
    public async Task The_link_survives_save_and_reopen()
    {
        string projectFile;
        Guid elementId;
        Guid sourceId;
        string expected;
        using (LinkHost host = await LinkHost.OpenAsync())
        {
            var (window, editor, element, source, target, _, _) = host;
            await LinkAsync(window, source.Opacity, target.Opacity);

            expected = $"GetProperty<float>(\"{{{target.Id.Value}}}.Opacity\")";
            elementId = element.Id.Value;
            sourceId = source.Id.Value;
            projectFile = host.Project.Uri!.LocalPath;

            Assert.That(await editor.SaveAsync(), Is.True);
            HeadlessTestHelpers.Settle();
        }

        EditViewModel reopened = await TestShell.ReopenAsync(projectFile);
        Element reopenedElement = reopened.Scene.Children.Single(item => item.Id.Value == elementId);
        var reopenedSource = reopenedElement.Objects.OfType<RectShape>().Single(item => item.Id.Value == sourceId);
        Assert.That(reopenedSource.Opacity.Expression?.ExpressionString, Is.EqualTo(expected));
    }

    // Opens the property panel of an element that holds the source and the target object, and picks through it.
    private sealed class LinkHost : IDisposable
    {
        private LinkHost(
            Project project, Window window, EditViewModel editor, Element element,
            RectShape source, RectShape target, ElementPropertyTabViewModel tab, ElementPropertyTabView view)
        {
            Project = project;
            Window = window;
            Editor = editor;
            Element = element;
            Source = source;
            Target = target;
            Tab = tab;
            View = view;
        }

        public Project Project { get; }
        public Window Window { get; }
        public EditViewModel Editor { get; }
        public Element Element { get; }
        public RectShape Source { get; }
        public RectShape Target { get; }
        public ElementPropertyTabViewModel Tab { get; }
        public ElementPropertyTabView View { get; }

        public static async Task<LinkHost> OpenAsync()
        {
            (Project project, Scene scene, EditViewModel editor) =
                await TestShell.OpenNewSceneAsync("property-link", duration: MediaTime.FromSeconds(6));
            await TestShell.AddElementAsync(
                editor, () => new RectShape { Opacity = { Value = 100 } }, MediaTime.Zero, MediaTime.FromSeconds(5));
            Element element = scene.Children.Single();
            var source = (RectShape)element.Objects[0];
            var target = new RectShape { Opacity = { Value = 25 } };
            element.AddObject(target);
            editor.HistoryManager.Commit("test setup");
            HeadlessTestHelpers.Settle();
            editor.GetRequiredService<IEditorSelection>().Select(element);
            HeadlessTestHelpers.Settle();

            var tab = new ElementPropertyTabViewModel(editor);
            tab.Element.Value = element;
            var view = new ElementPropertyTabView { DataContext = tab };
            var window = new Window
            {
                Content = view,
                Width = 720,
                Height = 860,
                RequestedThemeVariant = ThemeVariant.Light
            };
            try
            {
                window.Show();
                if (HeadlessCapture.IsRequested)
                    window.SetRenderScaling(1.5);
                HeadlessTestHelpers.Render(3);
                return new(project, window, editor, element, source, target, tab, view);
            }
            catch
            {
                window.Close();
                tab.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            Window.Close();
            Tab.Dispose();
        }

        public void Deconstruct(
            out Window window, out EditViewModel editor, out Element element, out RectShape source,
            out RectShape target, out ElementPropertyTabViewModel tab, out ElementPropertyTabView view)
        {
            window = Window;
            editor = Editor;
            element = Element;
            source = Source;
            target = Target;
            tab = Tab;
            view = View;
        }
    }

    private static IPropertyLinkSelection Selection(EditViewModel editor)
        => (IPropertyLinkSelection)editor.GetService(typeof(IPropertyLinkSelection))!;

    private static PropertyEditor RowOf(Visual scope, IProperty property)
        => scope.GetVisualDescendants().OfType<PropertyEditor>()
            .Single(row => row.DataContext is PropertyEditorViewModel viewModel
                && ReferenceEquals(viewModel.PropertyAdapter.GetEngineProperty(), property));

    private static PropertyLinkSelectionOverlay OverlayOf(PropertyEditor row)
        => row.GetVisualAncestors().OfType<PropertyLinkSelectionRow>().First().Children
            .OfType<PropertyLinkSelectionOverlay>().Single();

    private static async Task<PropertyEditor> ScrollToRowAsync(Visual scope, IProperty property)
    {
        PropertyEditor? row = null;
        await HeadlessTestHelpers.WaitUntilAsync(() =>
        {
            HeadlessTestHelpers.Render();
            row = scope.GetVisualDescendants().OfType<PropertyEditor>()
                .FirstOrDefault(candidate => candidate.DataContext is PropertyEditorViewModel viewModel
                    && ReferenceEquals(viewModel.PropertyAdapter.GetEngineProperty(), property));
            return row is { IsEffectivelyVisible: true, Bounds.Width: > 0 };
        }, message: $"The row of {property.Name} did not appear.");
        row!.BringIntoView();
        HeadlessTestHelpers.Render(3);
        return row!;
    }

    private static async Task HoverAsync(Window window, Control control)
    {
        Point point = HeadlessTestHelpers.Center(control, window);
        window.MouseMove(point);
        await HeadlessTestHelpers.WaitUntilAsync(() => control.IsPointerOver,
            message: $"The pointer did not reach {control}.");
        HeadlessTestHelpers.Render();
    }

    private static async Task StartLinkAsync(Visual scope, IProperty sourceProperty)
    {
        PropertyEditor sourceRow = await ScrollToRowAsync(scope, sourceProperty);
        PropertyEditorMenu menu = sourceRow.GetVisualDescendants().OfType<PropertyEditorMenu>().Single();
        Button button = menu.GetVisualDescendants().OfType<Button>().First();
        var flyout = (FAMenuFlyout)button.ContextFlyout!;
        flyout.ShowAt(button);
        HeadlessTestHelpers.Render();

        FAMenuFlyoutItem item = flyout.Items.OfType<FAMenuFlyoutItem>()
            .Single(menuItem => menuItem.Name == "linkToAnotherPropertyItem");
        Assert.That(item.IsVisible, Is.True, "The menu offers linking where it offers an expression.");
        item.RaiseEvent(new RoutedEventArgs(FAMenuFlyoutItem.ClickEvent));
        flyout.Hide();
        HeadlessTestHelpers.Render(2);
    }

    private static async Task LinkAsync(Visual scope, IProperty sourceProperty, IProperty targetProperty)
    {
        await StartLinkAsync(scope, sourceProperty);
        PropertyEditor targetRow = await ScrollToRowAsync(scope, targetProperty);
        HeadlessTestHelpers.Click(targetRow);
        HeadlessTestHelpers.Settle();
    }

    private static void PressEscape(Window window)
    {
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        HeadlessTestHelpers.Render(3);
    }

    private static void Capture(Window window, string name)
        => HeadlessCapture.SaveIfRequested(window, nameof(PropertyLinkSelectionTests), name);
}
