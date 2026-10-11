using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Animation;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.Graphics.Shapes;
using Beutl.PropertyAdapters;
using Beutl.Testing.Headless;
using Beutl.ViewModels.Editors;
using Beutl.Views.Editors;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class PropertyExpressionTests
{
    [AvaloniaTest]
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public void ExpressionFlyout_DisplaysFailuresAndPreservesAnimation(bool runtimeFailure, bool light)
    {
        var shape = new RectShape();
        var property = (AnimatableProperty<float>)shape.Opacity;
        property.CurrentValue = 75;
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float> { Value = 50 });
        property.Animation = animation;
        if (runtimeFailure)
        {
            property.Expression = new StringExpression<float>("1 / (int)Time");
            Assert.That(property.GetValue(new CompositionContext(TimeSpan.Zero)), Is.EqualTo(50));
        }

        using var model = new NumberEditorViewModel<float>(new AnimatablePropertyAdapter<float>(property, shape));
        var anchor = new Button { Content = "Opacity expression" };
        var window = new Window
        {
            Content = anchor,
            Width = 640,
            Height = 480,
            RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark
        };
        var flyout = new ExpressionEditorFlyout
        {
            ExpressionText = runtimeFailure ? model.GetExpressionString() : "\"abc\"",
            ErrorMessage = property.ExpressionError
        };
        flyout.Confirmed += (_, args) =>
        {
            args.IsValid = model.SetExpression(args.ExpressionText, out string? error);
            args.Error = error;
        };

        try
        {
            window.Show();
            flyout.ShowAt(anchor);
            HeadlessTestHelpers.Render();
            var presenter = (Control)typeof(ExpressionEditorFlyout)
                .GetField("_presenter", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(flyout)!;
            if (!runtimeFailure)
            {
                presenter.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "AcceptButton")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                HeadlessTestHelpers.Render();
            }

            var errorText = presenter.GetVisualDescendants().OfType<Avalonia.Controls.TextBlock>()
                .Single(text => text.Name == "ErrorTextBlock");
            Assert.Multiple(() =>
            {
                Assert.That(flyout.IsOpen, Is.True);
                Assert.That(errorText.IsVisible, Is.True);
                Assert.That(errorText.Text, Is.Not.Empty);
                Assert.That(property.Animation, Is.SameAs(animation));
                Assert.That(property.CurrentValue, Is.EqualTo(75));
                if (!runtimeFailure)
                {
                    Assert.That(property.Expression, Is.Null);
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
            flyout.Hide();
            window.Close();
        }
    }
}
