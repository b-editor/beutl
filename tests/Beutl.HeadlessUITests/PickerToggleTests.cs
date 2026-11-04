using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Beutl.Api.Services;
using Beutl.Audio;
using Beutl.Audio.Effects;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor;
using Beutl.Editor.Components.OutputTab.ViewModels;
using Beutl.Editor.Components.OutputTab.Views;
using Beutl.Editor.Components.PropertyEditors.Services;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.Components.PropertyEditors.Views;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.PropertyAdapters;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using FluentAvalonia.UI.Controls;
using static Beutl.HeadlessUITests.PropertyEditorTestHost;
using Color = Avalonia.Media.Color;
using Colors = Avalonia.Media.Colors;

namespace Beutl.HeadlessUITests;

// The pickers do not light-dismiss, so the button that opened one is the click that closes it again. Each editor sits
// at the top of a tall window, so a picker opens below its button as it does in the app and the pointer can reach the
// button again.
[TestFixture]
public sealed class PickerToggleTests
{
    [AvaloniaTest]
    public void The_color_button_closes_its_picker_and_drops_the_unconfirmed_color()
    {
        var editor = new ColorEditor { Header = "Color", Value = Colors.Red, IsLivePreviewEnabled = false };
        var confirmed = new List<EventArgs>();
        editor.ValueConfirmed += (_, e) => confirmed.Add(e);
        Window window = Show(editor);
        try
        {
            Button button = Part<Button>(editor, "PART_ColorPickerButton");

            Click(window, button);
            ((SimpleColorPicker)Single<SimpleColorPickerFlyoutPresenter>(window).Content!).Color = Colors.Blue;
            Click(window, button);

            Assert.Multiple(() =>
            {
                Assert.That(Open<SimpleColorPickerFlyoutPresenter>(window), Is.Empty);
                Assert.That(editor.Value, Is.EqualTo(Colors.Red));
                Assert.That(confirmed, Is.Empty);
            });

            Click(window, button);
            Color reopened = ((SimpleColorPicker)Single<SimpleColorPickerFlyoutPresenter>(window).Content!).Color;
            Assert.That(reopened, Is.EqualTo(Colors.Red));
        }
        finally
        {
            window.Close();
        }
    }

    // With live preview the picker has no confirm button: closing it confirms the color it shows.
    [AvaloniaTest]
    public void The_color_button_closes_its_live_picker_and_confirms_the_color()
    {
        var editor = new ColorEditor { Header = "Color", Value = Colors.Red, IsLivePreviewEnabled = true };
        var confirmed = new List<PropertyEditorValueChangedEventArgs<Color>>();
        editor.ValueConfirmed += (_, e) => confirmed.Add((PropertyEditorValueChangedEventArgs<Color>)e);
        Window window = Show(editor);
        try
        {
            Button button = Part<Button>(editor, "PART_ColorPickerButton");

            Click(window, button);
            ((SimpleColorPicker)Single<SimpleColorPickerFlyoutPresenter>(window).Content!).Color = Colors.Blue;
            Assert.That(editor.Value, Is.EqualTo(Colors.Blue));
            Click(window, button);

            Assert.Multiple(() =>
            {
                Assert.That(Open<SimpleColorPickerFlyoutPresenter>(window), Is.Empty);
                Assert.That(editor.Value, Is.EqualTo(Colors.Blue));
                Assert.That(confirmed.Select(e => (e.NewValue, e.OldValue)), Is.EqualTo(new[] { (Colors.Blue, Colors.Red) }));
            });
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public void The_grading_color_button_closes_its_picker()
    {
        var editor = new GradingColorEditor { Header = "Lift", Value = new GradingColor(0.1f, 0.2f, 0.3f), IsLivePreviewEnabled = false };
        Window window = Show(editor);
        try
        {
            AssertToggles<SimpleColorPickerFlyoutPresenter>(window, Part<Button>(editor, "PART_ColorPickerButton"));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public void The_brush_button_closes_its_editor()
    {
        var shape = new RectShape { Fill = { Value = new SolidColorBrush(Beutl.Media.Colors.White) } };
        using var history = new HistoryScope(shape);
        using var viewModel = new BrushEditorViewModel(
            new SimplePropertyAdapter<Brush?>((SimpleProperty<Brush?>)shape.Fill, shape));
        viewModel.Accept(ServiceVisitor.For(history.History));
        var editor = new BrushEditor { DataContext = viewModel, Width = 360, HorizontalAlignment = HorizontalAlignment.Left };
        Window window = Show(editor);
        try
        {
            AssertToggles<BrushEditorFlyoutPresenter>(window, editor.BrushPreviewButton);
        }
        finally
        {
            window.Close();
        }
    }

    // The type picker opens from a button in the brush's own picker, and closes with it.
    [AvaloniaTest]
    public void The_change_drawable_button_closes_its_picker_which_also_closes_with_the_brush_editor()
    {
        var shape = new RectShape { Fill = { Value = new DrawableBrush { Drawable = { Value = new EllipseShape() } } } };
        using var history = new HistoryScope(shape);
        using var viewModel = new BrushEditorViewModel(
            new SimplePropertyAdapter<Brush?>((SimpleProperty<Brush?>)shape.Fill, shape));
        viewModel.Accept(WithExtensions(history));
        var editor = new BrushEditor { DataContext = viewModel, Width = 360, HorizontalAlignment = HorizontalAlignment.Left };
        Window window = Show(editor);
        try
        {
            Click(window, editor.BrushPreviewButton);
            Button change = Part<Button>(Single<BrushEditorFlyoutPresenter>(window), "ChangeDrawableButton");
            AssertToggles<LibraryItemPickerFlyoutPresenter>(window, change);

            Click(window, change);
            Assert.That(Open<LibraryItemPickerFlyoutPresenter>(window), Has.Length.EqualTo(1));
            Click(window, editor.BrushPreviewButton);
            Assert.Multiple(() =>
            {
                Assert.That(Open<BrushEditorFlyoutPresenter>(window), Is.Empty);
                Assert.That(Open<LibraryItemPickerFlyoutPresenter>(window), Is.Empty);
            });

            // Closing with the brush editor leaves the editor able to open the picker again.
            Click(window, editor.BrushPreviewButton);
            Click(window, Part<Button>(Single<BrushEditorFlyoutPresenter>(window), "ChangeDrawableButton"));
            Assert.That(Open<LibraryItemPickerFlyoutPresenter>(window), Has.Length.EqualTo(1));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public void The_font_button_closes_its_picker_and_restores_the_previewed_font()
    {
        FontFamily initial = FontManager.Instance.FontFamilies.OrderBy(FontFamilyNames.GetDisplayName).First();
        var editor = new FontFamilyEditor { Header = "Font", Value = initial };
        var previewed = new List<FontFamily>();
        editor.ValueChanged += (_, e) => previewed.Add(((PropertyEditorValueChangedEventArgs<FontFamily>)e).NewValue);
        Window window = Show(editor);
        try
        {
            DropDownButton button = Part<DropDownButton>(editor, "PART_InnerButton");

            Click(window, button);
            Assert.That(Open<LibraryItemPickerFlyoutPresenter>(window), Has.Length.EqualTo(1));
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            HeadlessTestHelpers.Settle();
            Assume.That(previewed, Has.Count.EqualTo(1), "Needs a second installed font to preview.");
            Click(window, button);

            Assert.Multiple(() =>
            {
                Assert.That(Open<LibraryItemPickerFlyoutPresenter>(window), Is.Empty);
                Assert.That(editor.Value, Is.EqualTo(initial));
            });

            Click(window, button);
            Assert.That(Open<LibraryItemPickerFlyoutPresenter>(window), Has.Length.EqualTo(1));
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task The_profiles_button_closes_its_picker()
    {
        (_, _, EditViewModel edit) = await TestShell.OpenNewSceneAsync("picker-toggle-output");
        using var model = new OutputTabViewModel(edit);
        var view = new OutputTabView { DataContext = model };
        Window window = Show(view);
        try
        {
            AssertToggles<OutputPickerFlyoutPresenter>(window, view.FindControl<DropDownButton>("ProfilesButton")!);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public void The_add_button_of_a_filter_group_closes_its_picker()
    {
        var shape = new RectShape { FilterEffect = { Value = new FilterEffectGroup() } };
        using var history = new HistoryScope(shape);
        using var viewModel = new FilterEffectEditorViewModel(
            new SimplePropertyAdapter<FilterEffect?>((SimpleProperty<FilterEffect?>)shape.FilterEffect, shape));
        viewModel.Accept(WithExtensions(history));
        var editor = new FilterEffectEditor { DataContext = viewModel };
        Window window = Show(editor);
        try
        {
            EffectEditorHeaderButtons header = editor.GetVisualDescendants().OfType<EffectEditorHeaderButtons>().First();
            AssertToggles<LibraryItemPickerFlyoutPresenter>(
                window, header.GetVisualDescendants().OfType<Button>().Single(button => button is not ToggleButton));

            Assert.That(((FilterEffectGroup)shape.FilterEffect.Value!).Children, Is.Empty);
        }
        finally
        {
            window.Close();
        }
    }

    // A single effect takes the add button too: it goes into a group with what is added.
    [AvaloniaTest]
    public void The_add_button_of_a_single_filter_opens_its_picker_and_adds_beside_it()
    {
        var brightness = new Brightness();
        var shape = new RectShape { FilterEffect = { Value = brightness } };
        using var history = new HistoryScope(shape);
        using var viewModel = new FilterEffectEditorViewModel(
            new SimplePropertyAdapter<FilterEffect?>((SimpleProperty<FilterEffect?>)shape.FilterEffect, shape));
        viewModel.Accept(WithExtensions(history));
        var editor = new FilterEffectEditor { DataContext = viewModel };
        Window window = Show(editor);
        try
        {
            EffectEditorHeaderButtons header = editor.GetVisualDescendants().OfType<EffectEditorHeaderButtons>().First();
            AssertToggles<LibraryItemPickerFlyoutPresenter>(
                window, header.GetVisualDescendants().OfType<Button>().Single(button => button is not ToggleButton));

            viewModel.AddItem(typeof(Blur));

            var group = (FilterEffectGroup)shape.FilterEffect.Value!;
            Assert.Multiple(() =>
            {
                Assert.That(group.Children[0], Is.SameAs(brightness));
                Assert.That(group.Children[1], Is.TypeOf<Blur>());
                Assert.That(brightness.HierarchicalParent, Is.SameAs(group));
                Assert.That(((FilterEffectEditorViewModel)viewModel.Group.Value!.Items[1].Context!).IsExpanded.Value, Is.True);
            });
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public void The_add_button_of_an_unset_audio_effect_opens_its_picker_and_adds_to_a_new_group()
    {
        var sound = new SourceSound { Effect = { Value = null } };
        using var history = new HistoryScope(sound);
        using var viewModel = new AudioEffectEditorViewModel(new EnginePropertyAdapter<AudioEffect?>(sound.Effect, sound));
        viewModel.Accept(WithExtensions(history));
        var editor = new AudioEffectEditor { DataContext = viewModel };
        Window window = Show(editor);
        try
        {
            EffectEditorHeaderButtons header = editor.GetVisualDescendants().OfType<EffectEditorHeaderButtons>().First();
            AssertToggles<LibraryItemPickerFlyoutPresenter>(
                window, header.GetVisualDescendants().OfType<Button>().Single(button => button is not ToggleButton));

            viewModel.AddItem(new AudioEffectGroup());

            Assert.That(((AudioEffectGroup)sound.Effect.Value!).Children.Single(), Is.TypeOf<AudioEffectGroup>());
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public void The_add_button_of_an_audio_group_closes_its_picker()
    {
        var sound = new SourceSound { Effect = { Value = new AudioEffectGroup() } };
        using var viewModel = new AudioEffectEditorViewModel(new EnginePropertyAdapter<AudioEffect?>(sound.Effect, sound));
        viewModel.Accept(new ServiceVisitor((typeof(ExtensionProvider), TestShell.Extensions)));
        var editor = new AudioEffectEditor { DataContext = viewModel };
        Window window = Show(editor);
        try
        {
            EffectEditorHeaderButtons header = editor.GetVisualDescendants().OfType<EffectEditorHeaderButtons>().First();
            AssertToggles<LibraryItemPickerFlyoutPresenter>(
                window, header.GetVisualDescendants().OfType<Button>().Single(button => button is not ToggleButton));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public void The_new_button_of_an_object_editor_closes_its_picker()
    {
        var brush = new DrawableBrush();
        using var viewModel = new CoreObjectEditorViewModel<Drawable>(
            new SimplePropertyAdapter<Drawable>((SimpleProperty<Drawable>)brush.Drawable!, brush));
        var editor = new CoreObjectEditor { DataContext = viewModel };
        Window window = Show(editor);
        try
        {
            Button button = editor.GetVisualDescendants().OfType<Button>()
                .First(candidate => candidate is not ToggleButton && candidate.IsEffectivelyVisible);
            AssertToggles<LibraryItemPickerFlyoutPresenter>(window, button);
        }
        finally
        {
            window.Close();
        }
    }

    // A menu item is no button to click again: it opens the picker at the pointer and leaves an open one alone.
    [AvaloniaTest]
    public void The_change_menu_item_leaves_the_open_picker_alone()
    {
        var shape = new RectShape { FilterEffect = { Value = new Brightness() } };
        using var history = new HistoryScope(shape);
        using var viewModel = new FilterEffectEditorViewModel(
            new SimplePropertyAdapter<FilterEffect?>((SimpleProperty<FilterEffect?>)shape.FilterEffect, shape));
        viewModel.Accept(WithExtensions(history));
        var editor = new FilterEffectEditor { DataContext = viewModel };
        Window window = Show(editor);
        try
        {
            var menu = (FAMenuFlyout)editor.FindControl<ToggleButton>("expandToggle")!.ContextFlyout!;
            FAMenuFlyoutItem change = menu.Items.OfType<FAMenuFlyoutItem>().First();

            change.RaiseEvent(new RoutedEventArgs(FAMenuFlyoutItem.ClickEvent));
            HeadlessTestHelpers.Render();
            LibraryItemPickerFlyoutPresenter picker = Single<LibraryItemPickerFlyoutPresenter>(window);
            change.RaiseEvent(new RoutedEventArgs(FAMenuFlyoutItem.ClickEvent));
            HeadlessTestHelpers.Render();

            Assert.That(Single<LibraryItemPickerFlyoutPresenter>(window), Is.SameAs(picker));
        }
        finally
        {
            window.Close();
        }
    }

    private static Window Show(Control content)
    {
        content.VerticalAlignment = VerticalAlignment.Top;
        var window = new Window { Content = content, Width = 1000, Height = 900 };
        window.Show();
        HeadlessTestHelpers.Render();
        return window;
    }

    private static ServiceVisitor WithExtensions(HistoryScope history) => new(
        (typeof(HistoryManager), history.History),
        (typeof(IPropertyEditorFactory), new PropertyEditorFactoryAdapter(TestShell.Extensions)),
        (typeof(ExtensionProvider), TestShell.Extensions));

    private static T Part<T>(Control parent, string name) where T : Control
        => parent.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);

    // The window keeps its popups in its overlay layer, including one opened from a button in another popup.
    private static T[] Open<T>(Window window) where T : Control => [.. window.GetVisualDescendants().OfType<T>()];

    private static T Single<T>(Window window) where T : Control => Open<T>(window).Single();

    // Clicks the button with the pointer, after checking that an open picker leaves it uncovered.
    private static void Click(Window window, Control button)
    {
        TimelineTestHost.WaitForHitTest(window, button, HeadlessTestHelpers.Center(button, window));
        HeadlessTestHelpers.Click(button, ticks: 3);
    }

    private static void AssertToggles<TPresenter>(Window window, Control button) where TPresenter : Control
    {
        string name = TestContext.CurrentContext.Test.MethodName!;
        Click(window, button);
        HeadlessCapture.SaveIfRequested(window, nameof(PickerToggleTests), $"{name}-open");
        Assert.That(Open<TPresenter>(window), Has.Length.EqualTo(1), "The first click opens the picker.");
        Click(window, button);
        HeadlessCapture.SaveIfRequested(window, nameof(PickerToggleTests), $"{name}-closed");
        Assert.That(Open<TPresenter>(window), Is.Empty, "The second click closes the picker.");
        Click(window, button);
        Assert.That(Open<TPresenter>(window), Has.Length.EqualTo(1), "The third click opens it again.");
        Click(window, button);
        Assert.That(Open<TPresenter>(window), Is.Empty, "The fourth click closes it again.");
    }
}
