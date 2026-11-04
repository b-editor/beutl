using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Animation;
using Beutl.Composition;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.Components.PropertyEditors.Views;
using Beutl.Editor.PropertyEditors;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.Extensibility;
using Beutl.Graphics.Shapes;
using Beutl.PropertyAdapters;
using Beutl.Testing.Headless;
using FluentAvalonia.UI.Controls;
using Moq;
using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class PropertyExpressionTests
{
    // A control connects after the panel has handed the editor its clock, so the editor must keep the paused time.
    [AvaloniaTest]
    public void An_expression_connected_after_Accept_shows_its_value_at_the_paused_time()
    {
        var shape = new RectShape();
        var property = (AnimatableProperty<float>)shape.Opacity;
        property.Expression = new StringExpression<float>("(float)Time * 10");
        var clock = new Mock<IEditorClock>();
        clock.SetupGet(x => x.CurrentTime).Returns(new ReactivePropertySlim<MediaTime>(MediaTime.FromSeconds(2)));
        using var model = new FloatEditorViewModel((IPropertyAdapter<float>)PropertyAdapterFactory.CreateAdapter(property, shape));
        model.Accept(new PropertyEditorTestHost.ServiceVisitor((typeof(IEditorClock), clock.Object)));

        float shown = float.NaN;
        using IDisposable subscription = model.EditValue.Subscribe(value => shown = value);

        Assert.That(shown, Is.EqualTo(20f));
    }

    // The animation takes over from the expression, so it starts at the value on screen and not at one from before it.
    [AvaloniaTest]
    public void Editing_the_animation_of_an_expression_starts_at_the_value_it_shows()
    {
        var shape = new RectShape();
        var property = (AnimatableProperty<float>)shape.Opacity;
        property.Value = 75;
        property.Expression = new StringExpression<float>("(float)Time * 10");
        var clock = new Mock<IEditorClock>();
        clock.SetupGet(x => x.CurrentTime).Returns(new ReactivePropertySlim<MediaTime>(MediaTime.FromSeconds(2)));
        using var history = new PropertyEditorTestHost.HistoryScope(shape);
        using var model = new FloatEditorViewModel((IPropertyAdapter<float>)PropertyAdapterFactory.CreateAdapter(property, shape));
        model.Accept(new PropertyEditorTestHost.ServiceVisitor(
            (typeof(IEditorClock), clock.Object), (typeof(Beutl.Editor.HistoryManager), history.History)));

        model.PrepareToEditAnimation();

        Assert.Multiple(() =>
        {
            Assert.That(property.HasExpression, Is.False);
            Assert.That(((KeyFrame<float>)property.GetKeyFrameAnimation()!.KeyFrames.Single()).Value.Value, Is.EqualTo(20f));
        });
    }

    [AvaloniaTest]
    public void Editing_the_animation_of_a_fixed_value_starts_at_that_value()
    {
        var shape = new RectShape();
        var property = (AnimatableProperty<float>)shape.Opacity;
        property.Value = 75;
        using var history = new PropertyEditorTestHost.HistoryScope(shape);
        using var model = new FloatEditorViewModel((IPropertyAdapter<float>)PropertyAdapterFactory.CreateAdapter(property, shape));
        model.Accept(PropertyEditorTestHost.ServiceVisitor.For(history.History));

        model.PrepareToEditAnimation();

        Assert.That(((KeyFrame<float>)property.GetKeyFrameAnimation()!.KeyFrames.Single()).Value.Value, Is.EqualTo(75f));
    }

    [AvaloniaTest]
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public void ExpressionMenu_DisplaysFailuresAndPreservesAnimation(bool runtimeFailure, bool light)
    {
        var shape = new RectShape();
        var property = (AnimatableProperty<float>)shape.Opacity;
        property.Value = 75;
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float> { Value = { Value = 50 } });
        property.SetKeyFrameAnimation(animation);
        if (runtimeFailure)
        {
            property.Expression = new StringExpression<float>("1 / (int)Time");
            Assert.That(property.Evaluate(new CompositionContext(MediaTime.Zero)), Is.EqualTo(50));
        }

        using var model = new NumberEditorViewModel<float>(new AnimatablePropertyAdapter<float>(property, shape));
        var menu = new PropertyEditorMenu { DataContext = model };
        var window = new Window
        {
            Content = menu,
            Width = 640,
            Height = 480,
            RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark
        };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            Button button = menu.GetVisualDescendants().OfType<Button>().First();
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            HeadlessTestHelpers.Render();
            if (!runtimeFailure)
            {
                var contextMenu = (FAMenuFlyout)button.ContextFlyout!;
                contextMenu.Items.OfType<FAMenuFlyoutItem>().Single(item => item.Name == "editExpressionItem")
                    .RaiseEvent(new RoutedEventArgs(FAMenuFlyoutItem.ClickEvent));
                contextMenu.Hide();
                HeadlessTestHelpers.Render();
            }

            var popup = window.OpenedPopups.Single(popup => popup.Child is ExpressionEditorFlyoutPresenter);
            var presenter = (ExpressionEditorFlyoutPresenter)popup.Child!;
            if (!runtimeFailure)
            {
                presenter.GetVisualDescendants().OfType<TextBox>().Single(text => text.Name == "ExpressionTextBox")
                    .Text = "\"abc\"";
                presenter.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "AcceptButton")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                HeadlessTestHelpers.Render();
            }

            var errorText = presenter.GetVisualDescendants().OfType<Avalonia.Controls.TextBlock>()
                .Single(text => text.Name == "ErrorTextBlock");
            Assert.Multiple(() =>
            {
                Assert.That(popup.IsOpen, Is.True);
                Assert.That(errorText.IsVisible, Is.True);
                Assert.That(errorText.Text, Is.Not.Empty);
                Assert.That(property.GetKeyFrameAnimation(), Is.SameAs(animation));
                Assert.That(property.Value, Is.EqualTo(75));
                if (runtimeFailure)
                {
                    Assert.That(errorText.Text, Is.EqualTo(property.ExpressionError));
                    Assert.That(presenter.ExpressionText, Is.EqualTo(model.GetExpressionString()));
                }
                else
                {
                    Assert.That(property.HasExpression, Is.False);
                    Assert.That(errorText.Text, Does.Contain("string").And.Contain("float"));
                }
            });

            if (Environment.GetEnvironmentVariable("BEUTL_EXPRESSION_CAPTURE") is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
                using var image = TopLevel.GetTopLevel(presenter)!.CaptureRenderedFrame();
                Assert.That(image, Is.Not.Null);
                image!.Save(Path.Combine(directory, $"expression-{runtimeFailure}-{light}.png"), PngBitmapEncoderOptions.Default);
            }
        }
        finally
        {
            foreach (var popup in window.OpenedPopups.ToArray())
                popup.Close();
            window.Close();
        }
    }

    // The editor turns off light dismiss, so the fx button that opened it is the click that closes it again.
    [AvaloniaTest]
    public void The_fx_button_closes_the_expression_editor_it_opened()
    {
        var shape = new RectShape();
        var property = (AnimatableProperty<float>)shape.Opacity;
        property.Expression = new StringExpression<float>("50f");
        using var model = new NumberEditorViewModel<float>(new AnimatablePropertyAdapter<float>(property, shape));
        var menu = new PropertyEditorMenu { DataContext = model, VerticalAlignment = VerticalAlignment.Top };
        var window = new Window { Content = menu, Width = 640, Height = 800 };
        try
        {
            window.Show();
            Button button = menu.GetVisualDescendants().OfType<Button>().First();
            var center = HeadlessTestHelpers.Center(button, window);
            TimelineTestHost.WaitForHitTest(window, button, center);

            HeadlessTestHelpers.Click(button);
            var presenter = (ExpressionEditorFlyoutPresenter)ExpressionEditors(window).Single().Child!;
            presenter.ExpressionText = "100f";
            // The editor opens below the row, as in the app, so it leaves the fx button to click.
            TimelineTestHost.WaitForHitTest(window, button, center);
            HeadlessCapture.SaveIfRequested(window, nameof(PropertyExpressionTests), "fx-editor-open");
            HeadlessTestHelpers.Click(button);
            HeadlessCapture.SaveIfRequested(window, nameof(PropertyExpressionTests), "fx-clicked-again");
            Assert.Multiple(() =>
            {
                Assert.That(ExpressionEditors(window), Is.Empty, "The second click closes the editor.");
                Assert.That(property.Expression?.ExpressionString, Is.EqualTo("50f"), "Closing drops the edit, as × does.");
            });

            HeadlessTestHelpers.Click(button);
            Popup reopened = ExpressionEditors(window).Single();
            var reopenedPresenter = (ExpressionEditorFlyoutPresenter)reopened.Child!;
            Assert.That(reopenedPresenter.ExpressionText, Is.EqualTo("50f"));

            // The menu item that also opens the editor leaves the open one, and its edit, alone.
            reopenedPresenter.ExpressionText = "75f";
            HeadlessTestHelpers.Click(button, button: MouseButton.Right);
            var contextMenu = (FAMenuFlyout)button.ContextFlyout!;
            contextMenu.Items.OfType<FAMenuFlyoutItem>().Single(item => item.Name == "editExpressionItem")
                .RaiseEvent(new RoutedEventArgs(FAMenuFlyoutItem.ClickEvent));
            contextMenu.Hide();
            HeadlessTestHelpers.Render();
            Assert.Multiple(() =>
            {
                Assert.That(ExpressionEditors(window), Is.EqualTo(new[] { reopened }));
                Assert.That(reopenedPresenter.ExpressionText, Is.EqualTo("75f"));
            });

            // An editor closed by × leaves the fx button opening a new one.
            reopenedPresenter.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "DismissButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            HeadlessTestHelpers.Render();
            Assert.That(ExpressionEditors(window), Is.Empty);
            HeadlessTestHelpers.Click(button);
            Assert.That(ExpressionEditors(window).Count(), Is.EqualTo(1));
        }
        finally
        {
            foreach (var popup in window.OpenedPopups.ToArray())
                popup.Close();
            window.Close();
        }
    }

    private static IEnumerable<Popup> ExpressionEditors(TopLevel topLevel)
        => topLevel.OpenedPopups.Where(popup => popup.Child is ExpressionEditorFlyoutPresenter);

    // Derives from the typed base directly, as an extension's view model can, so its value is made when a control connects.
    private sealed class FloatEditorViewModel(IPropertyAdapter<float> property) : PropertyEditorViewModel<float, float>(property)
    {
        protected override float ToEdit(float value) => value;

        protected override float FromEdit(float edit) => edit;
    }
}
